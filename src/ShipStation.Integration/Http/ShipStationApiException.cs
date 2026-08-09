using System.Net;

namespace ShipStation.Integration.Http;

public sealed class ShipStationApiException : HttpRequestException
{
    public ShipStationApiException(HttpStatusCode statusCode, string message, string? responseBody)
        : base(message, inner: null, statusCode)
    {
        ResponseBody = responseBody;
    }

    /// <summary>
    /// Raw payload as returned by ShipStation. Their error shape is inconsistent
    /// between endpoints, so it is surfaced verbatim for logging rather than
    /// forced into a model that would only fit half the responses.
    /// </summary>
    public string? ResponseBody { get; }

    public static async Task<ShipStationApiException> FromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var reason = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : Truncate(body, 512);

        return new ShipStationApiException(
            response.StatusCode,
            $"ShipStation returned {(int)response.StatusCode} for {response.RequestMessage?.Method} " +
            $"{response.RequestMessage?.RequestUri?.AbsolutePath}: {reason}",
            body);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "…");
}
