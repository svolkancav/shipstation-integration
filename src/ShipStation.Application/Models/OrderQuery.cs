using System.Globalization;
using System.Text;

using ShipStation.Core.Enums;

namespace ShipStation.Application.Models;

/// <summary>
/// Filter for <c>GET /orders</c>. Only populated properties are sent — ShipStation
/// treats an empty query string value as a literal filter rather than "no filter",
/// which quietly returns zero rows.
/// </summary>
public sealed record OrderQuery
{
    public const int MaxPageSize = 500;

    public OrderStatus? Status { get; init; }

    public string? CustomerEmail { get; init; }

    public DateTimeOffset? ModifiedAfter { get; init; }

    public DateTimeOffset? ModifiedBefore { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 100;

    internal string ToRelativeUri()
    {
        var builder = new StringBuilder("orders?page=")
            .Append(Math.Max(Page, 1).ToString(CultureInfo.InvariantCulture))
            .Append("&pageSize=")
            .Append(Math.Clamp(PageSize, 1, MaxPageSize).ToString(CultureInfo.InvariantCulture));

        if (Status is { } status)
        {
            Append(builder, "orderStatus", ToApiValue(status));
        }

        if (!string.IsNullOrWhiteSpace(CustomerEmail))
        {
            Append(builder, "customerEmail", CustomerEmail);
        }

        // ShipStation expects these in the account's local time, without an offset.
        if (ModifiedAfter is { } after)
        {
            Append(builder, "modifyDateStart", FormatTimestamp(after));
        }

        if (ModifiedBefore is { } before)
        {
            Append(builder, "modifyDateEnd", FormatTimestamp(before));
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string ToApiValue(OrderStatus status) => status switch
    {
        OrderStatus.AwaitingPayment => "awaiting_payment",
        OrderStatus.AwaitingShipment => "awaiting_shipment",
        OrderStatus.Shipped => "shipped",
        OrderStatus.OnHold => "on_hold",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped order status.")
    };
}
