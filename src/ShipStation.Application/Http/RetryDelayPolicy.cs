using ShipStation.Application.Configuration;

namespace ShipStation.Application.Http;

/// <summary>
/// Decides how long to hold a throttled request. ShipStation is inconsistent
/// about which hint it sends — some endpoints set <c>Retry-After</c>, others only
/// refresh <c>X-Rate-Limit-Reset</c> — so the sources are tried in order of how
/// specific they are, and every result is capped by the caller's ceiling.
/// </summary>
internal static class RetryDelayPolicy
{
    internal const string ResetHeader = "X-Rate-Limit-Reset";

    internal static readonly TimeSpan Fallback = TimeSpan.FromSeconds(5);

    public static TimeSpan Resolve(HttpResponseMessage response, ShipStationOptions options, DateTimeOffset now)
    {
        var ceiling = options.MaxThrottleDelay;

        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Cap(delta, ceiling);
        }

        if (response.Headers.RetryAfter?.Date is { } date && date - now > TimeSpan.Zero)
        {
            return Cap(date - now, ceiling);
        }

        if (TryReadSeconds(response, ResetHeader, out var reset) && reset > 0)
        {
            return Cap(TimeSpan.FromSeconds(reset), ceiling);
        }

        return Cap(Fallback, ceiling);
    }

    internal static bool TryReadSeconds(HttpResponseMessage response, string header, out int value)
    {
        value = 0;

        return response.Headers.TryGetValues(header, out var values)
               && int.TryParse(values.FirstOrDefault(), out value);
    }

    private static TimeSpan Cap(TimeSpan value, TimeSpan ceiling) => value < ceiling ? value : ceiling;
}
