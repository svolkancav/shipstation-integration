using System.Text;
using Npgsql;
using NpgsqlTypes;
using ShipStation.Integration.Persistence.Entities;

namespace ShipStation.Integration.Persistence.Upsert;

/// <summary>
/// Builds a single <c>INSERT … ON CONFLICT DO UPDATE</c> statement for a batch of
/// orders. Pure: no connection, no I/O, so the wire format can be asserted directly.
/// </summary>
internal static class OrderUpsertCommand
{
    /// <summary>The PostgreSQL wire protocol caps a statement at 65535 parameters.</summary>
    internal const int ProtocolParameterLimit = 65535;

    private static readonly string[] Columns =
    [
        "order_id",
        "order_number",
        "order_key",
        "order_status",
        "customer_email",
        "order_total",
        "order_date",
        "modify_date",
        "payload",
        "synced_at"
    ];

    internal static int ColumnCount => Columns.Length;

    /// <summary>
    /// Largest batch that still fits inside the protocol's parameter limit.
    /// </summary>
    internal static int MaxRowsPerBatch => ProtocolParameterLimit / ColumnCount;

    /// <summary>
    /// Collapses duplicate keys, keeping the most recently modified copy of each.
    /// </summary>
    /// <remarks>
    /// PostgreSQL refuses an <c>ON CONFLICT DO UPDATE</c> that touches the same row
    /// twice in one statement — "cannot affect row a second time". A page of API
    /// results can legitimately contain the same order twice when it is modified
    /// mid-pagination, so this has to be handled here rather than hoped away.
    /// </remarks>
    internal static IReadOnlyList<OrderRecord> Deduplicate(IEnumerable<OrderRecord> records)
    {
        var latest = new Dictionary<long, OrderRecord>();

        foreach (var record in records)
        {
            if (!latest.TryGetValue(record.OrderId, out var existing)
                || (record.ModifyDate ?? DateTimeOffset.MinValue) >= (existing.ModifyDate ?? DateTimeOffset.MinValue))
            {
                latest[record.OrderId] = record;
            }
        }

        return [.. latest.Values];
    }

    public static NpgsqlCommand Build(IReadOnlyList<OrderRecord> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            throw new ArgumentException("Nothing to upsert.", nameof(batch));
        }

        if (batch.Count > MaxRowsPerBatch)
        {
            throw new ArgumentException(
                $"A batch of {batch.Count} rows needs {batch.Count * ColumnCount} parameters, " +
                $"over the protocol limit of {ProtocolParameterLimit}. Chunk to {MaxRowsPerBatch} or fewer.",
                nameof(batch));
        }

        var command = new NpgsqlCommand();
        var sql = new StringBuilder()
            .Append("INSERT INTO shipstation_orders (")
            .AppendJoin(", ", Columns)
            .Append(") VALUES ");

        for (var row = 0; row < batch.Count; row++)
        {
            if (row > 0)
            {
                sql.Append(", ");
            }

            sql.Append('(');

            for (var column = 0; column < ColumnCount; column++)
            {
                if (column > 0)
                {
                    sql.Append(", ");
                }

                sql.Append('@').Append('p').Append(row).Append('_').Append(column);
            }

            sql.Append(')');

            AddParameters(command, batch[row], row);
        }

        sql.Append(" ON CONFLICT (order_id) DO UPDATE SET ");

        // order_id is the conflict target, so it is never assigned.
        sql.AppendJoin(", ", Columns.Skip(1).Select(column => $"{column} = EXCLUDED.{column}"));

        // Skip rows that did not actually change. Without this guard a nightly
        // re-sync rewrites every row it touched, which churns WAL and leaves dead
        // tuples for autovacuum to clean up for no benefit. synced_at is excluded
        // from the comparison on purpose — it always differs.
        sql.Append(" WHERE shipstation_orders.modify_date IS DISTINCT FROM EXCLUDED.modify_date")
           .Append(" OR shipstation_orders.payload IS DISTINCT FROM EXCLUDED.payload");

        // xmax is zero on a freshly inserted tuple, which is how a single statement
        // can report inserts and updates apart. Rows filtered out by the WHERE above
        // are not returned at all, so they read as unchanged.
        sql.Append(" RETURNING (xmax = 0) AS inserted");

        command.CommandText = sql.ToString();

        return command;
    }

    private static void AddParameters(NpgsqlCommand command, OrderRecord record, int row)
    {
        Add(command, row, 0, NpgsqlDbType.Bigint, record.OrderId);
        Add(command, row, 1, NpgsqlDbType.Text, record.OrderNumber);
        Add(command, row, 2, NpgsqlDbType.Text, record.OrderKey);
        Add(command, row, 3, NpgsqlDbType.Text, record.OrderStatus);
        Add(command, row, 4, NpgsqlDbType.Text, record.CustomerEmail);
        Add(command, row, 5, NpgsqlDbType.Numeric, record.OrderTotal);
        Add(command, row, 6, NpgsqlDbType.TimestampTz, record.OrderDate);
        Add(command, row, 7, NpgsqlDbType.TimestampTz, record.ModifyDate);
        Add(command, row, 8, NpgsqlDbType.Jsonb, record.Payload);
        Add(command, row, 9, NpgsqlDbType.TimestampTz, record.SyncedAt);
    }

    private static void Add(NpgsqlCommand command, int row, int column, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter($"p{row}_{column}", type)
        {
            Value = value ?? DBNull.Value
        });
}
