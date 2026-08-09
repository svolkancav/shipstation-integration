using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShipStation.Integration.Configuration;

namespace ShipStation.Integration.Http;

/// <summary>
/// ShipStation publishes its remaining quota on every response. Reacting to that
/// is cheaper than absorbing 429s: once the remaining count reaches the configured
/// buffer we hold requests until the window resets, and a 429 that slips through
/// is retried against the reset hint rather than a fixed backoff.
/// </summary>
public sealed class RateLimitHandler : DelegatingHandler
{
    private const string LimitHeader = "X-Rate-Limit-Limit";
    private const string RemainingHeader = "X-Rate-Limit-Remaining";

    private readonly IOptionsMonitor<ShipStationOptions> _options;
    private readonly ILogger<RateLimitHandler> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _remaining = int.MaxValue;
    private DateTimeOffset _windowResetsAt = DateTimeOffset.MinValue;

    public RateLimitHandler(
        IOptionsMonitor<ShipStationOptions> options,
        ILogger<RateLimitHandler> logger,
        TimeProvider timeProvider)
    {
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        for (var attempt = 1; ; attempt++)
        {
            await WaitForQuotaAsync(options, cancellationToken).ConfigureAwait(false);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            CaptureQuota(response);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= options.MaxRetryAttempts)
            {
                return response;
            }

            var delay = RetryDelayPolicy.Resolve(response, options, _timeProvider.GetUtcNow());

            _logger.LogWarning(
                "ShipStation throttled {Method} {Path} (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                options.MaxRetryAttempts,
                delay);

            response.Dispose();
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForQuotaAsync(ShipStationOptions options, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_remaining > options.RateLimitBuffer)
            {
                return;
            }

            var wait = _windowResetsAt - _timeProvider.GetUtcNow();
            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            if (wait > options.MaxThrottleDelay)
            {
                wait = options.MaxThrottleDelay;
            }

            _logger.LogDebug("Quota exhausted ({Remaining} left); pausing {Wait} for the window to reset", _remaining, wait);
            await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);

            // The window has rolled over; let the next response correct this.
            _remaining = int.MaxValue;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CaptureQuota(HttpResponseMessage response)
    {
        if (RetryDelayPolicy.TryReadSeconds(response, RemainingHeader, out var remaining))
        {
            _remaining = remaining;
        }

        if (RetryDelayPolicy.TryReadSeconds(response, RetryDelayPolicy.ResetHeader, out var resetSeconds))
        {
            _windowResetsAt = _timeProvider.GetUtcNow().AddSeconds(resetSeconds);
        }

        if (_logger.IsEnabled(LogLevel.Trace) && RetryDelayPolicy.TryReadSeconds(response, LimitHeader, out var limit))
        {
            _logger.LogTrace("ShipStation quota {Remaining}/{Limit}", _remaining, limit);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }
}
