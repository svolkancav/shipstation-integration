using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShipStation.Integration.Http;
using ShipStation.Integration.Models;

namespace ShipStation.Integration.Orders;

internal sealed class ShipStationOrderClient : IShipStationOrderClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ShipStationOrderClient> _logger;

    public ShipStationOrderClient(HttpClient http, ILogger<ShipStationOrderClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var response = await _http.GetAsync(query.ToRelativeUri(), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await ReadAsync<OrderPage>(response, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<Order> EnumerateOrdersAsync(
        OrderQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var current = query;

        while (true)
        {
            var page = await GetOrdersAsync(current, cancellationToken).ConfigureAwait(false);

            foreach (var order in page.Orders)
            {
                yield return order;
            }

            if (!page.HasMore || page.Orders.Count == 0)
            {
                yield break;
            }

            current = current with { Page = page.Page + 1 };
        }
    }

    public async Task<Order> CreateOrUpdateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _http
            .PostAsJsonAsync("orders/createorder", request, SerializerOptions.Default, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var order = await ReadAsync<Order>(response, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Upserted order {OrderNumber} as ShipStation order {OrderId}",
            request.OrderNumber,
            order.OrderId);

        return order;
    }

    public async Task<bool> DeleteOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        using var response = await _http
            .DeleteAsync($"orders/{orderId}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Order {OrderId} was already absent from ShipStation", orderId);
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await ShipStationApiException.FromResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content
            .ReadFromJsonAsync<T>(SerializerOptions.Default, cancellationToken)
            .ConfigureAwait(false);

        return payload ?? throw new ShipStationApiException(
            response.StatusCode,
            $"ShipStation returned an empty body where {typeof(T).Name} was expected.",
            responseBody: null);
    }
}

internal static class SerializerOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
