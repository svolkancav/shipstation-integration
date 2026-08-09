# ShipStation Integration

A production-shaped ShipStation V1 client for .NET 10 — authentication, quota-aware
transport, and typed order operations, with a minimal API on top to exercise them.

[![CI](https://github.com/svolkancav/shipstation-integration/actions/workflows/ci.yml/badge.svg)](https://github.com/svolkancav/shipstation-integration/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)

> This is a reference implementation written against the public ShipStation API.
> It carries no credentials — every value in `appsettings.json` is a placeholder.

## Why this exists

Most sample integrations stop at "here is how you call the endpoint". The parts that
actually decide whether a sync survives contact with production are elsewhere:

- **ShipStation allows 40 requests per minute** and reports the remaining quota on
  every response. Waiting for a `429` before reacting wastes a request and a round
  trip. [`RateLimitHandler`](src/ShipStation.Integration/Http/RateLimitHandler.cs)
  reads `X-Rate-Limit-Remaining` as responses arrive and parks the next call once
  the quota nears exhaustion.
- **Throttling hints are inconsistent** between endpoints — some send `Retry-After`
  as seconds, some as a date, some send nothing and only refresh
  `X-Rate-Limit-Reset`. [`RetryDelayPolicy`](src/ShipStation.Integration/Http/RetryDelayPolicy.cs)
  resolves them in order of specificity and caps every result, so a bad header can
  never park a request for an hour.
- **`createorder` is an upsert, not a create.** ShipStation matches on `orderKey`
  and silently updates in place, which turns a retried request into a no-op instead
  of a duplicate — provided callers keep the key stable.
- **Paging is easy to get wrong.** `EnumerateOrdersAsync` streams pages lazily, so a
  caller that stops early stops spending quota.

## Layout

```
src/
  ShipStation.Integration/         client library — no ASP.NET dependency
    Authentication/                Basic auth handler
    Http/                          rate limiting, retry policy, error type
    Models/                        wire contracts
    Orders/                        the order client and its request/query types
  ShipStation.Integration.Persistence/
    Entities/                      the stored shape
    Upsert/                        SQL builder + batched store
    Sync/                          fetch -> add-or-update
  ShipStation.Integration.Api/     minimal API that exercises the client
tests/
  ShipStation.Integration.Tests/   42 tests, no network
```

## Usage

```csharp
builder.Services.AddShipStation(builder.Configuration);
```

Credentials are validated with `ValidateOnStart()`, so a misconfigured deployment
fails on boot rather than on the first order sync.

```csharp
public sealed class OrderSync(IShipStationOrderClient orders)
{
    public async Task<int> BackfillAsync(DateTimeOffset since, CancellationToken ct)
    {
        var query = new OrderQuery { ModifiedAfter = since, PageSize = 500 };
        var count = 0;

        await foreach (var order in orders.EnumerateOrdersAsync(query, ct))
        {
            count++;
        }

        return count;
    }
}
```

Creating and deleting:

```csharp
var order = await orders.CreateOrUpdateOrderAsync(new CreateOrderRequest
{
    OrderNumber = "SO-1001",
    OrderKey    = "erp:SO-1001",     // keeps retries idempotent
    OrderDate   = DateTimeOffset.UtcNow,
    BillTo      = address,
    ShipTo      = address,
    Items       = [item]
}, ct);

await orders.DeleteOrderAsync(order.OrderId, ct);   // false when already gone
```

## Configuration

```json
{
  "ShipStation": {
    "BaseAddress": "https://ssapi.shipstation.com",
    "ApiKey": "<YOUR_SHIPSTATION_API_KEY>",
    "ApiSecret": "<YOUR_SHIPSTATION_API_SECRET>",
    "RateLimitBuffer": 2,
    "MaxThrottleDelay": "00:01:10",
    "MaxRetryAttempts": 3,
    "Timeout": "00:01:40"
  }
}
```

| Setting | Meaning |
|---|---|
| `RateLimitBuffer` | Remaining-quota threshold at which the transport starts pacing |
| `MaxThrottleDelay` | Hard ceiling on any single throttle wait |
| `MaxRetryAttempts` | Total attempts per request, including the first |

For local work, keep secrets out of the repo:

```bash
dotnet user-secrets set "ShipStation:ApiKey" "…" --project src/ShipStation.Integration.Api
dotnet user-secrets set "ShipStation:ApiSecret" "…" --project src/ShipStation.Integration.Api
```

## Running

```bash
dotnet test                                     # 42 tests, no network access
dotnet run --project src/ShipStation.Integration.Api
```

The sample API exposes `GET /api/orders`, `GET /api/orders/stream`, `POST /api/orders`
and `DELETE /api/orders/{orderId}`, with API docs at `/scalar` in development.
Upstream failures are translated to `ProblemDetails` that preserve the original
status code — a caller retrying on 429 needs to see the 429, not a 500.

## Persisting what you fetch

Fetching is half the job. `*.Persistence` adds a PostgreSQL store with a batched
**add-or-update**: rows that are new get inserted, rows that changed get updated, and
rows that match what is already stored are left completely alone.

```csharp
builder.Services.AddShipStationPersistence(builder.Configuration);

var result = await sync.SyncAsync(since, ct);
// 120 inserted, 43 updated, 8017 unchanged
```

It is one `INSERT … ON CONFLICT (order_id) DO UPDATE` per batch, not a read-then-write
loop, so a page of 500 costs one round trip instead of a thousand.

Four things that are easy to get wrong and are handled here:

**The same row twice in one statement.** PostgreSQL rejects an `ON CONFLICT DO UPDATE`
that affects a row more than once — *"cannot affect row a second time"*. A record edited
mid-pagination legitimately appears on two consecutive pages, so the batch is
de-duplicated by key first, keeping the most recently modified copy.

**Rewriting rows that did not change.** The update carries a
`WHERE shipstation_orders.modify_date IS DISTINCT FROM EXCLUDED.modify_date` guard. Without it a
nightly re-sync rewrites every row it touches, churning WAL and leaving dead tuples for
autovacuum. `synced_at` is deliberately excluded from that comparison — it always differs,
and including it would defeat the guard entirely.

**Not knowing what happened.** `RETURNING (xmax = 0) AS inserted` distinguishes inserts
from updates in the same statement — `xmax` is zero only on a freshly inserted tuple.
Rows filtered out by the guard are not returned at all, which is exactly how they get
counted as unchanged. On a healthy steady-state sync, *unchanged* should dominate; if it
does not, change detection is broken and the job is burning I/O for nothing.

**The 65535-parameter ceiling.** The PostgreSQL wire protocol caps a statement's
parameters, so batches are chunked against that limit rather than assuming a page fits.
The sync service flushes every 500 records instead of accumulating — a backfill of a few
hundred thousand rows should not be held in memory to be written once.

The whole document is also kept in a `jsonb` column alongside the projected fields.
Integrations acquire *"we also need field X"* requirements constantly, and re-syncing a
year of history to backfill a column nobody modelled is worse than paying for the raw copy.

The SQL builder is a pure function, so the emitted statement, its parameter binding and
the batch limits are all asserted directly — no database needed in CI.

```json
{
  "ConnectionStrings": {
    "ShipStation": "Host=localhost;Database=shipstation;Username=<USER>;Password=<PASSWORD>"
  }
}
```

## Notes on scope

Only the order resource is modelled. Shipments, carriers, warehouses and labels follow
the same shape and would add volume without adding anything to read.

## License

MIT — see [LICENSE](LICENSE).
