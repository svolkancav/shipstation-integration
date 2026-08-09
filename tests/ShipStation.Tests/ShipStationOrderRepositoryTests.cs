using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShipStation.Core.Entities;
using ShipStation.Core.Enums;
using ShipStation.DataAccess;
using ShipStation.DataAccess.Repositories;
using ShipStation.DataAccess.Repositories.Impl;
using Xunit;

namespace ShipStation.Tests;

/// <summary>
/// Runs against SQLite in memory rather than a mock. The behaviour under test is
/// what EF's change tracker decides to write, and a mocked context cannot tell you
/// whether an unchanged entity would have produced an UPDATE.
/// </summary>
public sealed class ShipStationOrderRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDatabaseContext _context;
    private readonly IShipStationOrderRepository _repository;

    public ShipStationOrderRepositoryTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDatabaseContext(options);
        _context.Database.EnsureCreated();

        _repository = new ShipStationOrderRepository(_context, NullLogger<ShipStationOrderRepository>.Instance);
    }

    [Fact]
    public async Task Inserts_orders_it_has_never_seen()
    {
        var result = await _repository.AddOrUpdateAsync([Order(1), Order(2)], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(2, await _context.ShipStationOrders.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Updates_an_order_that_changed()
    {
        await _repository.AddOrUpdateAsync([Order(1, status: OrderStatus.AwaitingShipment)], TestContext.Current.CancellationToken);

        var result = await _repository.AddOrUpdateAsync(
            [Order(1, status: OrderStatus.Shipped, modified: new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc))],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(1, result.Updated);

        var stored = await _context.ShipStationOrders.AsNoTracking()
            .SingleAsync(order => order.OrderId == 1, TestContext.Current.CancellationToken);

        Assert.Equal(OrderStatus.Shipped, stored.OrderStatus);
        Assert.Single(_context.ShipStationOrders);
    }

    [Fact]
    public async Task Writes_nothing_when_the_order_came_back_identical()
    {
        var order = Order(1);
        await _repository.AddOrUpdateAsync([order], TestContext.Current.CancellationToken);

        // Same values again — the change tracker should find nothing to write.
        var result = await _repository.AddOrUpdateAsync([Order(1)], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Unchanged);
    }

    [Fact]
    public async Task Handles_a_mixed_batch()
    {
        await _repository.AddOrUpdateAsync([Order(1), Order(2)], TestContext.Current.CancellationToken);

        var result = await _repository.AddOrUpdateAsync(
            [
                Order(1),                                        // unchanged
                Order(2, status: OrderStatus.Cancelled),         // updated
                Order(3)                                         // inserted
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task Collapses_an_order_that_appears_twice_in_one_batch()
    {
        // Both copies share a key. Without de-duplication EF throws when the second
        // one is added to the tracker.
        var result = await _repository.AddOrUpdateAsync(
            [
                Order(1, status: OrderStatus.AwaitingShipment, modified: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
                Order(1, status: OrderStatus.Shipped, modified: new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc))
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.DuplicatesCollapsed);

        var stored = await _context.ShipStationOrders.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        // The newer copy wins.
        Assert.Equal(OrderStatus.Shipped, stored.OrderStatus);
    }

    [Fact]
    public async Task Returns_an_empty_result_for_an_empty_batch()
    {
        var result = await _repository.AddOrUpdateAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Processes_more_orders_than_fit_in_one_batch()
    {
        var many = Enumerable.Range(1, 1200).Select(i => Order(i)).ToArray();

        var result = await _repository.AddOrUpdateAsync(many, TestContext.Current.CancellationToken);

        Assert.Equal(1200, result.Inserted);
        Assert.Equal(1200, await _context.ShipStationOrders.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Watermark_is_null_before_anything_is_stored()
    {
        Assert.Null(await _repository.GetLatestModifyDateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Watermark_tracks_the_newest_modify_date()
    {
        await _repository.AddOrUpdateAsync(
            [
                Order(1, modified: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
                Order(2, modified: new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)),
                Order(3, modified: new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc))
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc),
            await _repository.GetLatestModifyDateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Order_status_is_stored_by_name_not_ordinal()
    {
        await _repository.AddOrUpdateAsync([Order(1, status: OrderStatus.OnHold)], TestContext.Current.CancellationToken);

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT OrderStatus FROM orders WHERE OrderId = 1";

        Assert.Equal("OnHold", (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static ShipStationOrder Order(
        long id,
        OrderStatus status = OrderStatus.AwaitingShipment,
        DateTime? modified = null) => new()
        {
            OrderId = id,
            OrderNumber = $"SO-{id}",
            OrderKey = $"erp:SO-{id}",
            OrderStatus = status,
            CustomerEmail = "ada@example.test",
            OrderTotal = 129.95m,
            OrderDate = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            ModifyDate = modified ?? new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            Payload = $$"""{"orderId":{{id}}}""",
            SyncedAt = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc)
        };

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
