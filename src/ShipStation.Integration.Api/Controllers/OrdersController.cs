using Microsoft.AspNetCore.Mvc;
using ShipStation.Integration.Models;
using ShipStation.Integration.Orders;

namespace ShipStation.Integration.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public sealed class OrdersController : ControllerBase
{
    private readonly IShipStationOrderClient _orders;

    public OrdersController(IShipStationOrderClient orders)
    {
        _orders = orders;
    }

    /// <summary>
    /// Returns a single page of orders straight from ShipStation.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<OrderPage>(StatusCodes.Status200OK)]
    public async Task<OrderPage> Get([FromQuery] OrderQuery query, CancellationToken cancellationToken) =>
        await _orders.GetOrdersAsync(query, cancellationToken);

    /// <summary>
    /// Streams every order matching the filter. Useful for backfills where the
    /// caller does not want to hold the whole result set in memory.
    /// </summary>
    [HttpGet("stream")]
    public IAsyncEnumerable<Order> Stream([FromQuery] OrderQuery query, CancellationToken cancellationToken) =>
        _orders.EnumerateOrdersAsync(query, cancellationToken);

    /// <summary>
    /// Creates an order, or updates it when the order key is already known.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<Order>(StatusCodes.Status201Created)]
    public async Task<ActionResult<Order>> Post(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _orders.CreateOrUpdateOrderAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { orderId = order.OrderId }, order);
    }

    [HttpDelete("{orderId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long orderId, CancellationToken cancellationToken) =>
        await _orders.DeleteOrderAsync(orderId, cancellationToken)
            ? NoContent()
            : NotFound();
}
