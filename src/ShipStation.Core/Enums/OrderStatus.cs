using System.Text.Json.Serialization;

namespace ShipStation.Core.Enums;

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
