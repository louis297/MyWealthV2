using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Users.Customers;

public class CreateCustomerLoginTests : TestBase
{
    [Test]
    public async Task HostedLogin_AfterCreate_CompletesAndCollectionStays403()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");

        var create = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                email = "Jordan@north.example",
                password = "Passw0rd!",
                adviserId = adviser.PublicId
            });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        created.RootElement.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);

        var login = await FunctionalTestSetup.Oidc.SignInAsync(
            "Jordan@north.example", "Passw0rd!", "north-advisory");
        login.AccessToken.ShouldNotBeNullOrEmpty();

        (await CustomerHttp.Send(HttpMethod.Get, "/users/customers", login.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var me = await CustomerHttp.Send(HttpMethod.Get, "/users/me", login.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
