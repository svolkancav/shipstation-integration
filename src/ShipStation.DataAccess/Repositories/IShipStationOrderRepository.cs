using ShipStation.Core;
using ShipStation.Core.Entities;

namespace ShipStation.DataAccess.Repositories;

public interface IShipStationOrderRepository
{
    /// <summary>
    /// Adds orders that are new and updates the ones that changed. Orders already
    /// stored with identical values are left alone.
    /// </summary>
    Task<UpsertResult> AddOrUpdateAsync(
        IReadOnlyCollection<ShipStationOrder> orders,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recent <see cref="ShipStationOrder.ModifyDate"/> on record, or
    /// <see langword="null"/> when nothing has been synced yet. Used as the
    /// watermark for the next incremental pull.
    /// </summary>
    Task<DateTime?> GetLatestModifyDateAsync(CancellationToken cancellationToken = default);
}
