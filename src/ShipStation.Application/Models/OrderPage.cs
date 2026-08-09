using System.Text.Json.Serialization;

namespace ShipStation.Application.Models;

public sealed record OrderPage
{
    [JsonPropertyName("orders")]
    public IReadOnlyList<OrderModel> Orders { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("pages")]
    public int Pages { get; init; }

    public bool HasMore => Page < Pages;
}
