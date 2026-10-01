extern alias BffHost;

using System.Net;
using BffHost::MyWealthV2.BffAdviserPortal;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public sealed class RecordingWebApi : IWebApiTransport
{
    public List<CapturedCall> Calls { get; } = [];

    public Dictionary<string, (HttpStatusCode Status, string Body)> Scripts { get; } = [];

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add(new CapturedCall(
            request.Method,
            request.RequestUri?.AbsolutePath ?? "",
            request.Headers.Authorization?.ToString(),
            request.Headers.Contains("Cookie"),
            request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
            request.Headers.Accept.ToString(),
            request.Content?.Headers.ContentType?.ToString()));

        var path = request.RequestUri?.AbsolutePath ?? "";
        if (Scripts.TryGetValue(path, out var script))
        {
            return Task.FromResult(new HttpResponseMessage(script.Status)
            {
                Content = new StringContent(script.Body, System.Text.Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        });
    }
}

public sealed record CapturedCall(
    HttpMethod Method,
    string Path,
    string? Authorization,
    bool HasCookie,
    string? IdempotencyKey,
    string Accept,
    string? ContentType);
