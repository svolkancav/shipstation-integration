using AutoMapper;
using Microsoft.Extensions.Logging;
using ShipStation.Application.Models;
using ShipStation.Core;
using ShipStation.Core.Entities;
using ShipStation.DataAccess.Repositories;

namespace ShipStation.Application.Services;

public class OrderSyncService(
    IShipStationOrderClient client,
    IShipStationOrderRepository repository,
    IMapper mapper,
    TimeProvider timeProvider,
    ILogger<OrderSyncService> logger) : IOrderSyncService
{
    /// <summary>
    /// How many orders are buffered before they are handed to the repository. A
    /// backfill can run to hundreds of thousands; holding all of them to write once
    /// is how a sync job earns an OOM.
    /// </summary>
    private const int FlushThreshold = 500;

    /// <summary>
    /// Overlap applied to the resumed watermark. ShipStation stamps modifyDate in the
    /// account's local time and its indexes lag by a beat, so resuming from the exact
    /// last value drops the orders written during the previous run's final second.
    /// Re-reading a minute is free — the upsert reports them as unchanged.
    /// </summary>
    private static readonly TimeSpan WatermarkOverlap = TimeSpan.FromMinutes(1);

    public async Task<UpsertResult> SyncAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        var from = since ?? await ResolveWatermarkAsync(cancellationToken);
        var query = new OrderQuery { ModifiedAfter = from, PageSize = OrderQuery.MaxPageSize };
        var syncedAt = timeProvider.GetUtcNow();

        var buffer = new List<ShipStationOrder>(FlushThreshold);
        var total = default(UpsertResult);

        await foreach (var model in client.EnumerateOrdersAsync(query, cancellationToken))
        {
            var entity = mapper.Map<ShipStationOrder>(model);
            entity.SyncedAt = syncedAt.UtcDateTime;

            buffer.Add(entity);

            if (buffer.Count >= FlushThreshold)
            {
                total += await repository.AddOrUpdateAsync(buffer, cancellationToken);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            total += await repository.AddOrUpdateAsync(buffer, cancellationToken);
        }

        logger.LogInformation("Synced orders modified since {Since:O}: {Result}", from, total);

        return total;
    }

    private async Task<DateTimeOffset> ResolveWatermarkAsync(CancellationToken cancellationToken)
    {
        var latest = await repository.GetLatestModifyDateAsync(cancellationToken);

        if (latest is null)
        {
            logger.LogInformation("No orders stored yet; starting from the epoch");
            return DateTimeOffset.UnixEpoch;
        }

        return new DateTimeOffset(latest.Value, TimeSpan.Zero) - WatermarkOverlap;
    }
}
