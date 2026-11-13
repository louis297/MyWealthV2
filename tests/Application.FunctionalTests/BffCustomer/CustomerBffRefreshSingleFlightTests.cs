extern alias BffCustomer;

using MyWealthV2.Application.FunctionalTests.Bff;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.BffCustomer;

public sealed class CustomerBffRefreshSingleFlightTests : RefreshSingleFlightScenarios
{
    private const string Password = "Password1!";

    protected override string ClientId => Services.CustomerPortal;

    protected override string ClientSecret => CustomerPortalTestSecret.Value;

    protected override string SessionCookie => "__Host-bff-customer-portal";

    protected override (IAsyncDisposable Host, HttpClient Client) StartBff(
        string authority,
        HttpMessageHandler identity,
        StaleBearerApi api,
        TimeProvider clock,
        string apiBaseAddress)
    {
        var factory = new CustomerBffFactory(
            authority,
            identity,
            api: new Transport(api),
            apiBaseAddress: apiBaseAddress,
            time: clock);
        return (factory, factory.CreateClient(BffClientOptions));
    }

    protected override async Task<(string Email, string Password, string TenantCode)> CreatePersonAsync()
    {
        var code = "cbff-" + Guid.NewGuid().ToString("N")[..8];
        var tenant = await TestApp.CreateTenantAsync("Firm " + code, code);
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@" + code, Password, tenant.Id);
        var email = "cee@" + code;
        await TestApp.CreatePersonAsync(UserRole.Customer, "Cee", email, Password, tenant.Id, adviser.Id);
        return (email, Password, code);
    }

    private sealed class Transport(StaleBearerApi api) : BffCustomer::MyWealthV2.BffCustomerPortal.IWebApiTransport
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            api.SendAsync(request, cancellationToken);
    }
}
