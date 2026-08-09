using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ShipStation.Application.Configuration;
using ShipStation.Application.Http;
using Xunit;

namespace ShipStation.Tests;

public sealed class RateLimitHandlerTests
{
    private static readonly Uri Endpoint = new("https://ssapi.example.test/orders");

    [Fact]
    public async Task Retries_after_a_429_and_returns_the_successful_response()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, headers: ("Retry-After", "3"))
            .Enqueue(HttpStatusCode.OK, "{}");

        var time = new FakeTimeProvider();
        using var client = CreateClient(stub, time);

        using var response = await DriveAsync(client.GetAsync(Endpoint, CancellationToken.None), time);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_once_the_attempt_budget_is_spent()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, headers: ("Retry-After", "1"))
            .Enqueue(HttpStatusCode.TooManyRequests, headers: ("Retry-After", "1"));

        var time = new FakeTimeProvider();
        using var client = CreateClient(stub, time, options => options.MaxRetryAttempts = 2);

        using var response = await DriveAsync(client.GetAsync(Endpoint, CancellationToken.None), time);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Does_not_sleep_while_quota_remains()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, "{}", ("X-Rate-Limit-Remaining", "30"), ("X-Rate-Limit-Reset", "50"))
            .Enqueue(HttpStatusCode.OK, "{}", ("X-Rate-Limit-Remaining", "29"), ("X-Rate-Limit-Reset", "49"));

        var time = new FakeTimeProvider();
        using var client = CreateClient(stub, time);

        using (await client.GetAsync(Endpoint, CancellationToken.None))
        {
        }

        var second = client.GetAsync(Endpoint, CancellationToken.None);

        // Nothing advanced the clock, so a request that waited would never finish.
        using var response = await second.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Holds_the_next_call_once_the_remaining_quota_hits_the_buffer()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, "{}", ("X-Rate-Limit-Remaining", "1"), ("X-Rate-Limit-Reset", "30"))
            .Enqueue(HttpStatusCode.OK, "{}", ("X-Rate-Limit-Remaining", "39"), ("X-Rate-Limit-Reset", "60"));

        var time = new FakeTimeProvider();
        using var client = CreateClient(stub, time, options => options.RateLimitBuffer = 2);

        using (await client.GetAsync(Endpoint, CancellationToken.None))
        {
        }

        var second = client.GetAsync(Endpoint, CancellationToken.None);

        // The second call is parked behind the reset, so it must not have hit the wire.
        await Task.Delay(50, CancellationToken.None);
        Assert.False(second.IsCompleted);
        Assert.Single(stub.Requests);

        using var response = await DriveAsync(second, time);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
        Assert.True(time.GetUtcNow() >= DateTimeOffset.UnixEpoch.AddSeconds(30));
    }

    /// <summary>
    /// The handler sleeps on the fake clock, so the test has to move it. Advance in
    /// small steps until the request settles rather than guessing the exact delay.
    /// </summary>
    private static async Task<HttpResponseMessage> DriveAsync(Task<HttpResponseMessage> pending, FakeTimeProvider time)
    {
        for (var tick = 0; tick < 500 && !pending.IsCompleted; tick++)
        {
            await Task.Delay(2, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(pending.IsCompleted, "The request never completed; the handler is likely waiting on a delay that was not advanced.");

        return await pending;
    }

    private static HttpClient CreateClient(
        StubHttpMessageHandler stub,
        FakeTimeProvider time,
        Action<ShipStationOptions>? configure = null)
    {
        var options = new ShipStationOptions { ApiKey = "key", ApiSecret = "secret" };
        configure?.Invoke(options);

        var handler = new RateLimitHandler(
            new StaticOptionsMonitor<ShipStationOptions>(options),
            NullLogger<RateLimitHandler>.Instance,
            time)
        {
            InnerHandler = stub
        };

        return new HttpClient(handler);
    }
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
