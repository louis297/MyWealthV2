using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Users.Customers;

public class CreateCustomerLoginTests : TestBase
{
    [Test]
    public async Task HostedLogin_AfterCreate_DoesNotIssueAdviserPortalCode()
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

        (await TestApp.CheckPasswordAsync("Jordan@north.example", "Passw0rd!", "north-advisory"))
            .ShouldBeTrue();

        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync(
            "Jordan@north.example", "Passw0rd!", "north-advisory");
        login.Code.ShouldBeNull();
    }
}
