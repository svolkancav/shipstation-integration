using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using ShipStation.Application.Configuration;

namespace ShipStation.Application.Http;

/// <summary>
/// ShipStation V1 authenticates with HTTP Basic over the API key/secret pair.
/// The credential is stable for the lifetime of the options instance, so it is
/// computed once and reused rather than re-encoded on every request.
/// </summary>
public sealed class ShipStationAuthenticationHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<ShipStationOptions> _options;
    private readonly Lock _gate = new();

    private string? _cachedCredential;
    private string? _cachedKey;

    public ShipStationAuthenticationHandler(IOptionsMonitor<ShipStationOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", GetCredential());
        return base.SendAsync(request, cancellationToken);
    }

    private string GetCredential()
    {
        var current = _options.CurrentValue;

        lock (_gate)
        {
            // Options can be reloaded from configuration at runtime; rebuild the
            // header only when the key actually changed.
            if (_cachedCredential is not null && _cachedKey == current.ApiKey)
            {
                return _cachedCredential;
            }

            var raw = $"{current.ApiKey}:{current.ApiSecret}";
            _cachedCredential = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            _cachedKey = current.ApiKey;

            return _cachedCredential;
        }
    }
}
