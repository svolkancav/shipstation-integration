using ShipStation.Application.Models;
using ShipStation.Core.Enums;
using ShipStation.Application.Services;
using Xunit;

namespace ShipStation.Tests;

public sealed class OrderQueryTests
{
    [Fact]
    public void Sends_only_paging_when_no_filter_is_set()
    {
        var uri = new OrderQuery().ToRelativeUri();

        Assert.Equal("orders?page=1&pageSize=100", uri);
    }

    [Fact]
    public void Omits_blank_filters_rather_than_sending_empty_values()
    {
        var uri = new OrderQuery { CustomerEmail = "   " }.ToRelativeUri();

        Assert.DoesNotContain("customerEmail", uri);
    }

    [Theory]
    [InlineData(OrderStatus.AwaitingPayment, "awaiting_payment")]
    [InlineData(OrderStatus.AwaitingShipment, "awaiting_shipment")]
    [InlineData(OrderStatus.OnHold, "on_hold")]
    [InlineData(OrderStatus.Cancelled, "cancelled")]
    public void Maps_status_to_the_snake_case_value_the_api_expects(OrderStatus status, string expected)
    {
        var uri = new OrderQuery { Status = status }.ToRelativeUri();

        Assert.Contains($"orderStatus={expected}", uri);
    }

    [Fact]
    public void Formats_timestamps_without_an_offset()
    {
        var uri = new OrderQuery
        {
            ModifiedAfter = new DateTimeOffset(2026, 3, 1, 14, 5, 9, TimeSpan.Zero)
        }.ToRelativeUri();

        Assert.Contains("modifyDateStart=2026-03-01%2014%3A05%3A09", uri);
    }

    [Fact]
    public void Escapes_values_that_would_otherwise_break_the_query_string()
    {
        var uri = new OrderQuery { CustomerEmail = "a+b@example.com" }.ToRelativeUri();

        Assert.Contains("customerEmail=a%2Bb%40example.com", uri);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(750, OrderQuery.MaxPageSize)]
    public void Clamps_page_size_to_the_documented_bounds(int requested, int expected)
    {
        var uri = new OrderQuery { PageSize = requested }.ToRelativeUri();

        Assert.Contains($"pageSize={expected}", uri);
    }
}
