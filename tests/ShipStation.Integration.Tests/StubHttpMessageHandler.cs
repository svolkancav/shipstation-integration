using System.Net;

namespace ShipStation.Integration.Tests;

/// <summary>
/// Replays a queued set of responses and records what was sent, so tests can
/// assert on the wire format without standing up a server.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public StubHttpMessageHandler Enqueue(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);

            if (json is not null)
            {
                response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"No stubbed response left for {request.Method} {request.RequestUri}.");
        }

        var response = _responses.Dequeue()(request);
        response.RequestMessage = request;

        return response;
    }
}
