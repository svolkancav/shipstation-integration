using System.Net;
using System.Net.Http.Headers;
using ShipStation.Integration.Configuration;
using ShipStation.Integration.Http;
using Xunit;

namespace ShipStation.Integration.Tests;

public sealed class RetryDelayPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Prefers_retry_after_when_the_server_sends_one()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
        response.Headers.TryAddWithoutValidation(RetryDelayPolicy.ResetHeader, "45");

        Assert.Equal(TimeSpan.FromSeconds(12), RetryDelayPolicy.Resolve(response, Options(), Now));
    }

    [Fact]
    public void Understands_retry_after_expressed_as_a_date()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(20), RetryDelayPolicy.Resolve(response, Options(), Now));
    }

    [Fact]
    public void Ignores_a_retry_after_date_that_has_already_passed()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(-30));
        response.Headers.TryAddWithoutValidation(RetryDelayPolicy.ResetHeader, "8");

        Assert.Equal(TimeSpan.FromSeconds(8), RetryDelayPolicy.Resolve(response, Options(), Now));
    }

    [Fact]
    public void Falls_back_to_the_rate_limit_reset_header()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation(RetryDelayPolicy.ResetHeader, "17");

        Assert.Equal(TimeSpan.FromSeconds(17), RetryDelayPolicy.Resolve(response, Options(), Now));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    [InlineData("")]
    public void Falls_back_to_a_fixed_delay_when_no_hint_is_usable(string reset)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation(RetryDelayPolicy.ResetHeader, reset);

        Assert.Equal(RetryDelayPolicy.Fallback, RetryDelayPolicy.Resolve(response, Options(), Now));
    }

    [Fact]
    public void Never_exceeds_the_configured_ceiling()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));

        var options = Options(o => o.MaxThrottleDelay = TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), RetryDelayPolicy.Resolve(response, options, Now));
    }

    private static ShipStationOptions Options(Action<ShipStationOptions>? configure = null)
    {
        var options = new ShipStationOptions { ApiKey = "key", ApiSecret = "secret" };
        configure?.Invoke(options);

        return options;
    }
}
