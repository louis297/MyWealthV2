namespace MyWealthV2.BffAdviserPortal;

public sealed class WebApiTransport(IHttpClientFactory factory) : IWebApiTransport, IDisposable
{
    private readonly HttpMessageInvoker _direct = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Scheme.Contains('+', StringComparison.Ordinal) == true)
        {
            return factory.CreateClient("bff-webapi").SendAsync(request, cancellationToken);
        }

        return _direct.SendAsync(request, cancellationToken);
    }

    public void Dispose() => _direct.Dispose();
}
