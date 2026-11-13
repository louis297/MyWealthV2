extern alias BffHost;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public sealed class BffRefreshSingleFlightTests : RefreshSingleFlightScenarios
{
    protected override string ClientId => Services.AdviserPortal;

    protected override string ClientSecret => AdviserPortalTestSecret.Value;

    protected override string SessionCookie => "__Host-bff-adviser-portal";

    protected override (IAsyncDisposable Host, HttpClient Client) StartBff(
        string authority,
        HttpMessageHandler identity,
        StaleBearerApi api,
        TimeProvider clock,
        string apiBaseAddress)
    {
        var factory = new BffFactory(
            authority,
            identity,
            api: new Transport(api),
            apiBaseAddress: apiBaseAddress,
            time: clock);
        return (factory, factory.CreateClient(BffClientOptions));
    }

    protected override async Task<(string Email, string Password, string TenantCode)> CreatePersonAsync()
    {
        var staff = await BffStaffLogin.CreateAsync();
        return (staff.Email, BffStaffLogin.Password, staff.TenantCode);
    }

    private sealed class Transport(StaleBearerApi api) : BffHost::MyWealthV2.BffAdviserPortal.IWebApiTransport
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            api.SendAsync(request, cancellationToken);
    }
}
