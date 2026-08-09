using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShipStation.Integration.Models;
using ShipStation.Integration.Orders;
using ShipStation.Integration.Persistence.Entities;
using ShipStation.Integration.Persistence.Upsert;

namespace ShipStation.Integration.Persistence.Sync;

public interface IOrderSyncService
{
    /// <summary>
    /// Pulls every order modified since <paramref name="since"/> and adds or updates
    /// it locally.
    /// </summary>
    Task<UpsertResult> SyncAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}

internal sealed class OrderSyncService : IOrderSyncService
{
    private const int FlushThreshold = 500;

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly IShipStationOrderClient _client;
    private readonly IOrderStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderSyncService> _logger;

    public OrderSyncService(
        IShipStationOrderClient client,
        IOrderStore store,
        TimeProvider timeProvider,
        ILogger<OrderSyncService> logger)
    {
        _client = client;
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UpsertResult> SyncAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var query = new OrderQuery { ModifiedAfter = since, PageSize = OrderQuery.MaxPageSize };
        var syncedAt = _timeProvider.GetUtcNow();

        var buffer = new List<OrderRecord>(FlushThreshold);
        var total = default(UpsertResult);

        // Written to the store in fixed-size flushes rather than accumulated: a
        // backfill can run to hundreds of thousands of orders, and holding all of
        // them to write once is how a sync job earns an OOM.
        await foreach (var order in _client.EnumerateOrdersAsync(query, cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(ToRecord(order, syncedAt));

            if (buffer.Count >= FlushThreshold)
            {
                total += await _store.AddOrUpdateAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            total += await _store.AddOrUpdateAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Synced orders modified since {Since:O}: {Result}", since, total);

        return total;
    }

    private static OrderRecord ToRecord(Order order, DateTimeOffset syncedAt) =>
        OrderRecord.FromOrder(order, JsonSerializer.Serialize(order, PayloadOptions), syncedAt);
}
