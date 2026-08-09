using ShipStation.Integration.Models;

namespace ShipStation.Integration.Persistence.Entities;

/// <summary>
/// A ShipStation order as stored locally.
/// </summary>
/// <remarks>
/// The columns that get queried are projected out; the whole document is kept in
/// <see cref="Payload"/> as <c>jsonb</c>. Integrations acquire new "we also need
/// field X" requirements constantly, and re-syncing a year of orders to backfill a
/// column nobody modelled is far worse than paying for the raw copy up front.
/// </remarks>
public sealed class OrderRecord
{
    /// <summary>ShipStation's own identifier — the natural key, so no surrogate.</summary>
    public long OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public string? OrderKey { get; set; }

    public string OrderStatus { get; set; } = string.Empty;

    public string? CustomerEmail { get; set; }

    public decimal OrderTotal { get; set; }

    public DateTimeOffset OrderDate { get; set; }

    /// <summary>
    /// ShipStation's last-modified stamp. Drives change detection, so a re-sync of
    /// untouched orders writes nothing.
    /// </summary>
    public DateTimeOffset? ModifyDate { get; set; }

    public string Payload { get; set; } = "{}";

    public DateTimeOffset SyncedAt { get; set; }

    public static OrderRecord FromOrder(Order order, string payload, DateTimeOffset syncedAt) => new()
    {
        OrderId = order.OrderId,
        OrderNumber = order.OrderNumber,
        OrderKey = order.OrderKey,
        OrderStatus = order.OrderStatus.ToString(),
        CustomerEmail = order.CustomerEmail,
        OrderTotal = order.OrderTotal,
        OrderDate = order.OrderDate,
        ModifyDate = order.ModifyDate,
        Payload = payload,
        SyncedAt = syncedAt
    };
}
