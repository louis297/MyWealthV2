using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

public class GetNetWorthTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var firm = await AccountHttp.Send(HttpMethod.Get, "/net-worth", accessToken: null);
        firm.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        firm.Headers.Location.ShouldBeNull();

        var customer = await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{Guid.NewGuid()}/net-worth", accessToken: null);
        customer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        customer.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        (await AccountHttp.Send(HttpMethod.Get, "/net-worth", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{customer.PublicId}/net-worth", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
