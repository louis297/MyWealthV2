namespace MyWealthV2.BffAdviserPortal;

public interface IWebApiTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
