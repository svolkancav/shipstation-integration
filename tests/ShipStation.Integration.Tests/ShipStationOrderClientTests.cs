using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ShipStation.Integration.Http;
using ShipStation.Integration.Models;
using ShipStation.Integration.Orders;
using Xunit;

namespace ShipStation.Integration.Tests;

public sealed class ShipStationOrderClientTests
{
    [Fact]
    public async Task Get_orders_maps_the_page_envelope()
    {
        const string payload = """
        {
          "orders": [
            {
              "orderId": 91234,
              "orderNumber": "SO-1001",
              "orderDate": "2026-02-14T09:30:00.0000000",
              "orderStatus": "awaiting_shipment",
              "orderTotal": 129.95,
              "items": [{ "sku": "WIDGET-1", "name": "Widget", "quantity": 2, "unitPrice": 59.99 }]
            }
          ],
          "total": 1,
          "page": 1,
          "pages": 1
        }
        """;

        var (client, stub) = CreateClient(h => h.Enqueue(HttpStatusCode.OK, payload));

        var page = await client.GetOrdersAsync(new OrderQuery(), TestContext.Current.CancellationToken);

        var order = Assert.Single(page.Orders);
        Assert.Equal(91234, order.OrderId);
        Assert.Equal("SO-1001", order.OrderNumber);
        Assert.Equal(OrderStatus.AwaitingShipment, order.OrderStatus);
        Assert.Equal(129.95m, order.OrderTotal);
        Assert.Equal("WIDGET-1", Assert.Single(order.Items).Sku);
        Assert.False(page.HasMore);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Enumerate_walks_every_page_then_stops()
    {
        var (client, stub) = CreateClient(h => h
            .Enqueue(HttpStatusCode.OK, Page(page: 1, pages: 2, orderId: 1))
            .Enqueue(HttpStatusCode.OK, Page(page: 2, pages: 2, orderId: 2)));

        var ids = new List<long>();

        await foreach (var order in client.EnumerateOrdersAsync(new OrderQuery(), TestContext.Current.CancellationToken))
        {
            ids.Add(order.OrderId);
        }

        Assert.Equal([1L, 2L], ids);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Contains("page=2", stub.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Enumerate_stops_paging_when_the_consumer_breaks_out()
    {
        var (client, stub) = CreateClient(h => h
            .Enqueue(HttpStatusCode.OK, Page(page: 1, pages: 5, orderId: 1)));

        await foreach (var order in client.EnumerateOrdersAsync(new OrderQuery(), TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, order.OrderId);
            break;
        }

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Create_posts_to_the_upsert_endpoint_and_omits_null_properties()
    {
        // Unlike the list endpoint, createorder responds with a bare order.
        var (client, stub) = CreateClient(h => h.Enqueue(HttpStatusCode.OK, """
        {
          "orderId": 555,
          "orderNumber": "SO-2002",
          "orderDate": "2026-02-14T09:30:00.0000000",
          "orderStatus": "awaiting_shipment",
          "orderTotal": 10.0,
          "items": []
        }
        """));

        var order = await client.CreateOrUpdateOrderAsync(new CreateOrderRequest
        {
            OrderNumber = "SO-2002",
            OrderDate = new DateTimeOffset(2026, 2, 14, 9, 30, 0, TimeSpan.Zero),
            BillTo = new Address { Name = "Ada Lovelace", Street1 = "1 Analytical Way", City = "Ankara", State = "06", PostalCode = "06000", Country = "TR" },
            ShipTo = new Address { Name = "Ada Lovelace", Street1 = "1 Analytical Way", City = "Ankara", State = "06", PostalCode = "06000", Country = "TR" },
            Items = [new OrderItem { Sku = "WIDGET-1", Name = "Widget", Quantity = 1, UnitPrice = 10m }]
        }, TestContext.Current.CancellationToken);

        Assert.Equal(555, order.OrderId);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("orders/createorder", request.RequestUri!.AbsolutePath);

        var body = stub.RequestBodies[0];
        Assert.Contains("\"orderNumber\":\"SO-2002\"", body);
        Assert.DoesNotContain("\"orderKey\"", body);
        Assert.DoesNotContain("\"internalNotes\"", body);
    }

    [Fact]
    public async Task Delete_reports_false_when_the_order_is_already_gone()
    {
        var (client, _) = CreateClient(h => h.Enqueue(HttpStatusCode.NotFound));

        Assert.False(await client.DeleteOrderAsync(404, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_reports_true_on_success()
    {
        var (client, _) = CreateClient(h => h.Enqueue(HttpStatusCode.OK, "{\"success\":true}"));

        Assert.True(await client.DeleteOrderAsync(91234, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Errors_carry_the_status_code_and_the_raw_body()
    {
        var (client, _) = CreateClient(h =>
            h.Enqueue(HttpStatusCode.BadRequest, """{"ExceptionMessage":"orderDate is required"}"""));

        var error = await Assert.ThrowsAsync<ShipStationApiException>(
            () => client.GetOrdersAsync(new OrderQuery(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("orderDate is required", error.ResponseBody);
    }

    private static (IShipStationOrderClient Client, StubHttpMessageHandler Stub) CreateClient(
        Action<StubHttpMessageHandler> arrange)
    {
        var stub = new StubHttpMessageHandler();
        arrange(stub);

        var http = new HttpClient(stub) { BaseAddress = new Uri("https://ssapi.example.test/") };

        return (new ShipStationOrderClient(http, NullLogger<ShipStationOrderClient>.Instance), stub);
    }

    private static string Page(int page, int pages, long orderId) => $$"""
    {
      "orders": [
        {
          "orderId": {{orderId}},
          "orderNumber": "SO-{{orderId}}",
          "orderDate": "2026-02-14T09:30:00.0000000",
          "orderStatus": "shipped",
          "orderTotal": 10.0,
          "items": []
        }
      ],
      "total": {{pages}},
      "page": {{page}},
      "pages": {{pages}}
    }
    """;
}
