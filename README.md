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

Four layers, the same split used across the services this was extracted from:

```
src/
  ShipStation.Core/          entities and enums — depends on nothing
    Entities/                ShipStationOrder
    Enums/                   OrderStatus
  ShipStation.DataAccess/    EF Core over PostgreSQL
    AppDatabaseContext.cs
    Repositories/            interfaces
    Repositories/Impl/       implementations
  ShipStation.Application/   the integration itself
    Configuration/           options + validation
    Http/                    auth handler, rate limiting, retry policy, error type
    Models/                  wire contracts and queries
    Services/                the API client and the sync orchestration
    MappingProfiles/         AutoMapper: wire model -> entity
  ShipStation.API/           controllers, DI wiring, problem details
tests/
  ShipStation.Tests/         39 tests, no network and no database server
```

Dependencies point inward: `API -> Application -> DataAccess -> Core`. Core references
nothing, so the domain can be tested and reused without dragging EF or HTTP along.

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
dotnet test                                     # 39 tests, no network access
dotnet run --project src/ShipStation.Integration.Api
```

The sample API exposes `GET /api/orders`, `GET /api/orders/stream`, `POST /api/orders`
and `DELETE /api/orders/{orderId}`, with API docs at `/scalar` in development.
Upstream failures are translated to `ProblemDetails` that preserve the original
status code — a caller retrying on 429 needs to see the 429, not a 500.

## Persisting what you fetch

Fetching is half the job. `ShipStation.DataAccess` stores orders in PostgreSQL through
EF Core with a batched **add-or-update**: new orders are inserted, changed ones updated,
and orders that came back identical are left completely alone.

```csharp
builder.Services
    .AddDataAccess(builder.Configuration)
    .AddApplication(builder.Configuration)
    .AddServices()
    .RegisterProfiles();

var result = await sync.SyncAsync();          // resumes from the stored watermark
// 120 inserted, 43 updated, 8017 unchanged
```

The repository loads a batch's existing rows in one query, then hands each incoming
order to the change tracker:

```csharp
if (stored.TryGetValue(incoming.OrderId, out var existing))
    context.Entry(existing).CurrentValues.SetValues(incoming);
else
    await context.ShipStationOrders.AddAsync(incoming, cancellationToken);
```

`SetValues` copies the scalars and lets EF work out what actually differs — an order that
came back unchanged produces **no UPDATE statement at all**, so a nightly re-sync does not
rewrite rows it did not need to touch. The insert/update/unchanged split is read off
`ChangeTracker` before `SaveChanges`, which is also what the endpoint returns.

Four things that are easy to get wrong and are handled here:

**The same order twice in one batch.** A record modified mid-pagination legitimately comes
back on two consecutive pages. Both copies would land in the change tracker under one key
and EF throws on the second add, so the batch is de-duplicated first, keeping the most
recently modified copy.

**Unbounded change tracking.** `ChangeTracker.Clear()` runs between batches. Without it a
backfill keeps every entity it has ever seen alive for the length of the job.

**Enums stored as integers.** `OrderStatus` is converted to its name. Storing the ordinal
couples the column to declaration order — reorder the enum and every stored row silently
changes meaning.

**A watermark with no overlap.** Resuming from the exact last `ModifyDate` drops orders
written during the previous run's final second. The watermark is rewound a minute; the
re-read costs nothing because those rows come back as unchanged.

Timestamps are stored as UTC `DateTime` rather than `DateTimeOffset` — ShipStation sends
`modifyDate` without an offset, so carrying one would imply precision the source does not
have.

```json
{
  "ConnectionStrings": {
    "ShipStation": "Host=localhost;Port=5432;Database=shipstation;Username=<DB_USER>;Password=<DB_PASSWORD>"
  }
}
```

The repository tests run against SQLite in memory rather than a mock: the behaviour under
test is what EF decides to write, and a mocked context cannot tell you whether an unchanged
entity would have produced an UPDATE.

## Notes on scope

Only the order resource is modelled. Shipments, carriers, warehouses and labels follow
the same shape and would add volume without adding anything to read.

## License

MIT — see [LICENSE](LICENSE).
