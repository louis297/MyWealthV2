namespace MyWealthV2.BffCustomerPortal;

public interface IWebApiTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
