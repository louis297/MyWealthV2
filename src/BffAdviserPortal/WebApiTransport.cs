namespace MyWealthV2.BffAdviserPortal;

public sealed class WebApiTransport(IHttpClientFactory factory) : IWebApiTransport
{
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        factory.CreateClient("bff-webapi").SendAsync(request, cancellationToken);
}
