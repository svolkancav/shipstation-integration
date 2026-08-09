using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShipStation.Core;
using ShipStation.Core.Entities;

namespace ShipStation.DataAccess.Repositories.Impl;

public class ShipStationOrderRepository(AppDatabaseContext context, ILogger<ShipStationOrderRepository> logger)
    : IShipStationOrderRepository
{
    /// <summary>
    /// Kept well inside PostgreSQL's 65535-parameter statement ceiling; the loaded
    /// slice also bounds how much the change tracker holds at once.
    /// </summary>
    private const int BatchSize = 500;

    public async Task<UpsertResult> AddOrUpdateAsync(
        IReadOnlyCollection<ShipStationOrder> orders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orders);

        if (orders.Count == 0)
        {
            return default;
        }

        var deduplicated = Deduplicate(orders);
        var total = new UpsertResult(0, 0, 0, orders.Count - deduplicated.Count);

        if (total.DuplicatesCollapsed > 0)
        {
            logger.LogDebug("Collapsed {Count} duplicate order(s) within the batch", total.DuplicatesCollapsed);
        }

        foreach (var batch in deduplicated.Chunk(BatchSize))
        {
            total += await ProcessBatchAsync(batch, cancellationToken);
        }

        logger.LogInformation("Order sync: {Result}", total);

        return total;
    }

    public async Task<DateTime?> GetLatestModifyDateAsync(CancellationToken cancellationToken = default) =>
        // ORDER BY … DESC LIMIT 1 rather than MAX(): it walks the ModifyDate index
        // to the first row, and unlike MAX() it translates on every provider.
        await context.ShipStationOrders
            .AsNoTracking()
            .OrderByDescending(order => order.ModifyDate)
            .Select(order => order.ModifyDate)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<UpsertResult> ProcessBatchAsync(
        ShipStationOrder[] batch,
        CancellationToken cancellationToken)
    {
        var ids = batch.Select(order => order.OrderId).ToArray();

        // One query for the whole batch instead of a lookup per order.
        var stored = await context.ShipStationOrders
            .Where(order => ids.Contains(order.OrderId))
            .ToDictionaryAsync(order => order.OrderId, cancellationToken);

        foreach (var incoming in batch)
        {
            if (stored.TryGetValue(incoming.OrderId, out var existing))
            {
                // SetValues copies scalar values and lets the change tracker decide
                // what actually differs. An order that came back from the API
                // unchanged produces no UPDATE at all, so a nightly re-sync does not
                // rewrite rows it did not need to touch.
                context.Entry(existing).CurrentValues.SetValues(incoming);
            }
            else
            {
                await context.ShipStationOrders.AddAsync(incoming, cancellationToken);
            }
        }

        // Counted before SaveChanges — afterwards every entry is Unchanged.
        var entries = context.ChangeTracker.Entries<ShipStationOrder>().ToArray();
        var inserted = entries.Count(entry => entry.State == EntityState.Added);
        var updated = entries.Count(entry => entry.State == EntityState.Modified);

        await context.SaveChangesAsync(cancellationToken);

        // The tracker is cleared so the next batch starts with an empty graph;
        // otherwise a long backfill keeps every entity it has ever seen alive.
        context.ChangeTracker.Clear();

        return new UpsertResult(inserted, updated, batch.Length - inserted - updated, 0);
    }

    /// <summary>
    /// Keeps the most recently modified copy of each order id.
    /// </summary>
    /// <remarks>
    /// A page of API results can contain the same order twice when it is modified
    /// mid-pagination. Left alone, both copies land in the change tracker under one
    /// key and EF throws on the second Add.
    /// </remarks>
    private static IReadOnlyList<ShipStationOrder> Deduplicate(IEnumerable<ShipStationOrder> orders)
    {
        var latest = new Dictionary<long, ShipStationOrder>();

        foreach (var order in orders)
        {
            if (!latest.TryGetValue(order.OrderId, out var existing)
                || (order.ModifyDate ?? DateTime.MinValue) >= (existing.ModifyDate ?? DateTime.MinValue))
            {
                latest[order.OrderId] = order;
            }
        }

        return [.. latest.Values];
    }
}
