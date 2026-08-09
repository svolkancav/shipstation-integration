using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShipStation.Integration.Persistence.Entities;

namespace ShipStation.Integration.Persistence.Upsert;

public interface IOrderStore
{
    /// <summary>
    /// Adds orders that are new and updates the ones that changed, in batches, inside
    /// a single transaction. Orders already stored unchanged are left alone.
    /// </summary>
    Task<UpsertResult> AddOrUpdateAsync(
        IReadOnlyCollection<OrderRecord> records,
        CancellationToken cancellationToken = default);
}

internal sealed class OrderStore : IOrderStore
{
    private readonly ShipStationDbContext _context;
    private readonly ILogger<OrderStore> _logger;
    private readonly int _batchSize;

    public OrderStore(ShipStationDbContext context, ILogger<OrderStore> logger, int batchSize = 500)
    {
        _context = context;
        _logger = logger;

        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, OrderUpsertCommand.MaxRowsPerBatch);

        _batchSize = batchSize;
    }

    public async Task<UpsertResult> AddOrUpdateAsync(
        IReadOnlyCollection<OrderRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return default;
        }

        var deduplicated = OrderUpsertCommand.Deduplicate(records);
        var collapsed = records.Count - deduplicated.Count;

        if (collapsed > 0)
        {
            _logger.LogDebug("Collapsed {Collapsed} duplicate order(s) within the batch", collapsed);
        }

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        // One transaction for the whole set: a half-applied sync leaves the watermark
        // ahead of the data, and the next run would skip the gap.
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var total = new UpsertResult(0, 0, 0, collapsed);

        try
        {
            foreach (var batch in Chunk(deduplicated, _batchSize))
            {
                total += await ExecuteBatchAsync(connection, transaction, batch, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Order sync: {Result}", total);

        return total;
    }

    private static async Task<UpsertResult> ExecuteBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<OrderRecord> batch,
        CancellationToken cancellationToken)
    {
        await using var command = OrderUpsertCommand.Build(batch);

        command.Connection = connection;
        command.Transaction = transaction;

        var inserted = 0;
        var updated = 0;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetBoolean(0))
                {
                    inserted++;
                }
                else
                {
                    updated++;
                }
            }
        }

        // Anything the statement did not return was filtered by the change guard.
        return new UpsertResult(inserted, updated, batch.Count - inserted - updated, 0);
    }

    private static IEnumerable<IReadOnlyList<OrderRecord>> Chunk(IReadOnlyList<OrderRecord> records, int size)
    {
        for (var offset = 0; offset < records.Count; offset += size)
        {
            yield return records.Skip(offset).Take(size).ToArray();
        }
    }
}
