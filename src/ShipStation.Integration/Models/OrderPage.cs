using System.Text.Json.Serialization;

namespace ShipStation.Integration.Models;

public sealed record OrderPage
{
    [JsonPropertyName("orders")]
    public IReadOnlyList<Order> Orders { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("pages")]
    public int Pages { get; init; }

    public bool HasMore => Page < Pages;
}
