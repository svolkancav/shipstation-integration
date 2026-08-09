using System.Text.Json.Serialization;

namespace ShipStation.Integration.Models;

public sealed record Order
{
    [JsonPropertyName("orderId")]
    public long OrderId { get; init; }

    [JsonPropertyName("orderNumber")]
    public string OrderNumber { get; init; } = string.Empty;

    [JsonPropertyName("orderKey")]
    public string? OrderKey { get; init; }

    [JsonPropertyName("orderDate")]
    public DateTimeOffset OrderDate { get; init; }

    [JsonPropertyName("orderStatus")]
    public OrderStatus OrderStatus { get; init; }

    [JsonPropertyName("customerEmail")]
    public string? CustomerEmail { get; init; }

    [JsonPropertyName("orderTotal")]
    public decimal OrderTotal { get; init; }

    [JsonPropertyName("billTo")]
    public Address? BillTo { get; init; }

    [JsonPropertyName("shipTo")]
    public Address? ShipTo { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<OrderItem> Items { get; init; } = [];

    [JsonPropertyName("modifyDate")]
    public DateTimeOffset? ModifyDate { get; init; }
}

public sealed record Address
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("company")]
    public string? Company { get; init; }

    [JsonPropertyName("street1")]
    public string Street1 { get; init; } = string.Empty;

    [JsonPropertyName("street2")]
    public string? Street2 { get; init; }

    [JsonPropertyName("city")]
    public string City { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; init; } = "US";

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    /// <summary>
    /// ShipStation reports address validation asynchronously; a freshly created
    /// order comes back with this unset until their validator has run.
    /// </summary>
    [JsonPropertyName("addressVerified")]
    public string? AddressVerified { get; init; }
}

public sealed record OrderItem
{
    [JsonPropertyName("orderItemId")]
    public long? OrderItemId { get; init; }

    [JsonPropertyName("sku")]
    public string Sku { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; init; }

    [JsonPropertyName("warehouseLocation")]
    public string? WarehouseLocation { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    [JsonStringEnumMemberName("awaiting_payment")]
    AwaitingPayment,

    [JsonStringEnumMemberName("awaiting_shipment")]
    AwaitingShipment,

    [JsonStringEnumMemberName("shipped")]
    Shipped,

    [JsonStringEnumMemberName("on_hold")]
    OnHold,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled
}
