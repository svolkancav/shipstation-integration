using ShipStation.Application.Models;

namespace ShipStation.Application.Services;

public interface IShipStationOrderClient
{
    /// <summary>
    /// Fetches a single page of orders.
    /// </summary>
    Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every page matching <paramref name="query"/>, starting from the page
    /// it specifies. Each page is fetched only as the consumer pulls it, so a
    /// caller that stops early stops spending quota.
    /// </summary>
    IAsyncEnumerable<OrderModel> EnumerateOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an order. ShipStation matches on
    /// <see cref="CreateOrderRequest.OrderKey"/> when one is supplied.
    /// </summary>
    Task<OrderModel> CreateOrUpdateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes an order. Returns <see langword="false"/> when ShipStation
    /// has no record of it, which is the common case when a delete is replayed.
    /// </summary>
    Task<bool> DeleteOrderAsync(long orderId, CancellationToken cancellationToken = default);
}
