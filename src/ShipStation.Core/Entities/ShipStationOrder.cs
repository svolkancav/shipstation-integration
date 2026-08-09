using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ShipStation.Core.Enums;

namespace ShipStation.Core.Entities;

/// <summary>
/// A ShipStation order as stored locally.
/// </summary>
/// <remarks>
/// The fields that get queried are mapped to columns; the whole document is kept in
/// <see cref="Payload"/> as jsonb. Integrations acquire "we also need field X"
/// requirements constantly, and re-syncing a year of orders to backfill a column
/// nobody modelled costs far more than keeping the raw copy.
/// </remarks>
[Table("orders", Schema = "shipstation")]
public class ShipStationOrder
{
    /// <summary>ShipStation's own identifier — the natural key, so no surrogate.</summary>
    [Key]
    public long OrderId { get; set; }

    [Required]
    [MaxLength(100)]
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Key the upstream system joins on. Unique where present, but nullable — which
    /// is why the index that enforces it has to be filtered.
    /// </summary>
    [MaxLength(100)]
    public string? OrderKey { get; set; }

    public OrderStatus OrderStatus { get; set; }

    [MaxLength(256)]
    public string? CustomerEmail { get; set; }

    public decimal OrderTotal { get; set; }

    public DateTime OrderDate { get; set; }

    /// <summary>
    /// ShipStation's last-modified stamp, stored as a UTC instant. ShipStation sends
    /// it without an offset, so carrying a DateTimeOffset here would imply precision
    /// the source does not have. Doubles as the incremental sync watermark.
    /// </summary>
    public DateTime? ModifyDate { get; set; }

    public string Payload { get; set; } = "{}";

    public DateTime SyncedAt { get; set; }
}
