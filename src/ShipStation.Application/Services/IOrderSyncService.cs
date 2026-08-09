using ShipStation.Core;

namespace ShipStation.Application.Services;

public interface IOrderSyncService
{
    /// <summary>
    /// Pulls every order modified since <paramref name="since"/> and adds or updates
    /// it locally. Pass <see langword="null"/> to resume from the stored watermark.
    /// </summary>
    Task<UpsertResult> SyncAsync(DateTimeOffset? since = null, CancellationToken cancellationToken = default);
}
