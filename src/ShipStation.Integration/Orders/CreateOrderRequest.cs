using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ShipStation.Integration.Models;

namespace ShipStation.Integration.Orders;

/// <summary>
/// Payload for <c>POST /orders/createorder</c>. The endpoint is an upsert:
/// ShipStation matches on <see cref="OrderKey"/> and updates in place when it
/// already knows the key, so callers should keep it stable per source order.
/// </summary>
public sealed record CreateOrderRequest
{
    [Required(AllowEmptyStrings = false)]
    [JsonPropertyName("orderNumber")]
    public required string OrderNumber { get; init; }

    [JsonPropertyName("orderKey")]
    public string? OrderKey { get; init; }

    [JsonPropertyName("orderDate")]
    public required DateTimeOffset OrderDate { get; init; }

    [JsonPropertyName("orderStatus")]
    public OrderStatus OrderStatus { get; init; } = OrderStatus.AwaitingShipment;

    [JsonPropertyName("customerEmail")]
    public string? CustomerEmail { get; init; }

    [JsonPropertyName("billTo")]
    public required Address BillTo { get; init; }

    [JsonPropertyName("shipTo")]
    public required Address ShipTo { get; init; }

    [MinLength(1)]
    [JsonPropertyName("items")]
    public required IReadOnlyList<OrderItem> Items { get; init; }

    [JsonPropertyName("amountPaid")]
    public decimal? AmountPaid { get; init; }

    [JsonPropertyName("internalNotes")]
    public string? InternalNotes { get; init; }
}
