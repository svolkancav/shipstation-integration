using ShipStation.Integration.Persistence.Entities;
using ShipStation.Integration.Persistence.Upsert;
using Xunit;

namespace ShipStation.Integration.Tests;

internal static class OrderRecords
{
    public static OrderRecord Record(long id, DateTimeOffset? modifyDate = null, string orderNumber = "SO-1") => new()
    {
        OrderId = id,
        OrderNumber = orderNumber,
        OrderStatus = "shipped",
        OrderTotal = 10m,
        OrderDate = new DateTimeOffset(2026, 2, 14, 9, 30, 0, TimeSpan.Zero),
        ModifyDate = modifyDate,
        Payload = """{"orderId":1}""",
        SyncedAt = new DateTimeOffset(2026, 3, 5, 8, 0, 0, TimeSpan.Zero)
    };
}

public sealed class OrderUpsertCommandTests
{
    private static OrderRecord Record(long id, DateTimeOffset? modifyDate = null, string orderNumber = "SO-1") =>
        OrderRecords.Record(id, modifyDate, orderNumber);

    [Fact]
    public void Emits_an_insert_on_conflict_update_for_the_natural_key()
    {
        using var command = OrderUpsertCommand.Build([Record(1)]);

        Assert.Contains("INSERT INTO shipstation_orders", command.CommandText);
        Assert.Contains("ON CONFLICT (order_id) DO UPDATE SET", command.CommandText);
    }

    [Fact]
    public void Never_reassigns_the_conflict_target()
    {
        using var command = OrderUpsertCommand.Build([Record(1)]);

        var updateClause = command.CommandText[command.CommandText.IndexOf("DO UPDATE SET", StringComparison.Ordinal)..];

        Assert.DoesNotContain("order_id = EXCLUDED.order_id", updateClause);
        Assert.Contains("order_number = EXCLUDED.order_number", updateClause);
        Assert.Contains("payload = EXCLUDED.payload", updateClause);
    }

    [Fact]
    public void Guards_the_update_so_unchanged_rows_are_not_rewritten()
    {
        using var command = OrderUpsertCommand.Build([Record(1)]);

        Assert.Contains(
            "WHERE shipstation_orders.modify_date IS DISTINCT FROM EXCLUDED.modify_date",
            command.CommandText);
        Assert.Contains("OR shipstation_orders.payload IS DISTINCT FROM EXCLUDED.payload", command.CommandText);

        // synced_at always differs, so including it would defeat the guard entirely.
        Assert.DoesNotContain("synced_at IS DISTINCT FROM", command.CommandText);
    }

    [Fact]
    public void Returns_a_flag_that_separates_inserts_from_updates()
    {
        using var command = OrderUpsertCommand.Build([Record(1)]);

        Assert.EndsWith("RETURNING (xmax = 0) AS inserted", command.CommandText);
    }

    [Fact]
    public void Parameterises_every_value()
    {
        using var command = OrderUpsertCommand.Build([Record(1), Record(2), Record(3)]);

        Assert.Equal(3 * OrderUpsertCommand.ColumnCount, command.Parameters.Count);
        Assert.Contains("(@p0_0, @p0_1", command.CommandText);
        Assert.Contains("(@p2_0, @p2_1", command.CommandText);

        // A hostile order number must never reach the statement text.
        using var hostile = OrderUpsertCommand.Build([Record(9, orderNumber: "'); DROP TABLE shipstation_orders;--")]);

        Assert.DoesNotContain("DROP TABLE", hostile.CommandText);
        Assert.Contains(
            hostile.Parameters.Cast<Npgsql.NpgsqlParameter>(),
            parameter => Equals(parameter.Value, "'); DROP TABLE shipstation_orders;--"));
    }

    [Fact]
    public void Rejects_a_batch_that_would_exceed_the_protocol_parameter_limit()
    {
        var oversized = Enumerable.Range(0, OrderUpsertCommand.MaxRowsPerBatch + 1)
            .Select(i => Record(i))
            .ToArray();

        var error = Assert.Throws<ArgumentException>(() => OrderUpsertCommand.Build(oversized));

        Assert.Contains("over the protocol limit", error.Message);
    }

    [Fact]
    public void Accepts_a_batch_exactly_at_the_limit()
    {
        var maximum = Enumerable.Range(0, OrderUpsertCommand.MaxRowsPerBatch)
            .Select(i => Record(i))
            .ToArray();

        using var command = OrderUpsertCommand.Build(maximum);

        Assert.True(command.Parameters.Count <= OrderUpsertCommand.ProtocolParameterLimit);
    }

    [Fact]
    public void Rejects_an_empty_batch()
    {
        Assert.Throws<ArgumentException>(() => OrderUpsertCommand.Build([]));
    }
}

public sealed class DeduplicationTests
{
    [Fact]
    public void Collapses_repeated_keys_keeping_the_newest_copy()
    {
        // Pagination can hand back the same order twice when it is modified between
        // pages; PostgreSQL rejects a statement that touches one row twice.
        var records = new[]
        {
            Record(1, modifyDate: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), orderNumber: "old"),
            Record(2),
            Record(1, modifyDate: new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), orderNumber: "new")
        };

        var deduplicated = OrderUpsertCommand.Deduplicate(records);

        Assert.Equal(2, deduplicated.Count);
        Assert.Equal("new", Assert.Single(deduplicated, record => record.OrderId == 1).OrderNumber);
    }

    [Fact]
    public void Keeps_the_later_occurrence_when_neither_has_a_modify_date()
    {
        var records = new[]
        {
            Record(1, modifyDate: null, orderNumber: "first"),
            Record(1, modifyDate: null, orderNumber: "second")
        };

        Assert.Equal("second", Assert.Single(OrderUpsertCommand.Deduplicate(records)).OrderNumber);
    }

    [Fact]
    public void Leaves_distinct_keys_alone()
    {
        var records = new[] { Record(1), Record(2), Record(3) };

        Assert.Equal(3, OrderUpsertCommand.Deduplicate(records).Count);
    }

    private static OrderRecord Record(long id, DateTimeOffset? modifyDate = null, string orderNumber = "SO-1") =>
        OrderRecords.Record(id, modifyDate, orderNumber);
}

public sealed class UpsertResultTests
{
    [Fact]
    public void Sums_across_batches()
    {
        var total = new UpsertResult(2, 3, 5, 1) + new UpsertResult(1, 1, 2, 0);

        Assert.Equal(3, total.Inserted);
        Assert.Equal(4, total.Updated);
        Assert.Equal(7, total.Unchanged);
        Assert.Equal(1, total.DuplicatesCollapsed);
        Assert.Equal(7, total.Affected);
        Assert.Equal(14, total.Total);
    }

    [Fact]
    public void Mentions_collapsed_duplicates_only_when_there_were_any()
    {
        Assert.DoesNotContain("duplicates", new UpsertResult(1, 0, 0, 0).ToString());
        Assert.Contains("2 duplicates collapsed", new UpsertResult(1, 0, 0, 2).ToString());
    }
}
