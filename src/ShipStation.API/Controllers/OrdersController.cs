using Microsoft.AspNetCore.Mvc;
using ShipStation.Application.Models;
using ShipStation.Application.Services;
using ShipStation.Core;

namespace ShipStation.API.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController(IShipStationOrderClient orders, IOrderSyncService sync) : ControllerBase
{
    /// <summary>Returns a single page of orders straight from ShipStation.</summary>
    [HttpGet]
    [ProducesResponseType<OrderPage>(StatusCodes.Status200OK)]
    public async Task<OrderPage> Get([FromQuery] OrderQuery query, CancellationToken cancellationToken) =>
        await orders.GetOrdersAsync(query, cancellationToken);

    /// <summary>
    /// Streams every order matching the filter without holding the result set in memory.
    /// </summary>
    [HttpGet("stream")]
    public IAsyncEnumerable<OrderModel> Stream([FromQuery] OrderQuery query, CancellationToken cancellationToken) =>
        orders.EnumerateOrdersAsync(query, cancellationToken);

    /// <summary>Creates an order, or updates it when the order key is already known.</summary>
    [HttpPost]
    [ProducesResponseType<OrderModel>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderModel>> Post(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await orders.CreateOrUpdateOrderAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { orderId = order.OrderId }, order);
    }

    [HttpDelete("{orderId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long orderId, CancellationToken cancellationToken) =>
        await orders.DeleteOrderAsync(orderId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>
    /// Pulls orders modified since the given point and stores them. Omit
    /// <c>since</c> to resume from the stored watermark.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType<UpsertResult>(StatusCodes.Status200OK)]
    public async Task<UpsertResult> Sync([FromQuery] DateTimeOffset? since, CancellationToken cancellationToken) =>
        await sync.SyncAsync(since, cancellationToken);
}
