using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Users.Advisers;

public class CreateAdviserLoginTests : TestBase
{
    [Test]
    public async Task HostedLogin_AfterCreate_CompletesWithTenantCodeEmailAndPassword()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");

        var create = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, new
            {
                name = "Sam Reed",
                email = "Sam@north.example",
                password = "Passw0rd!"
            });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        created.RootElement.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);

        var login = await FunctionalTestSetup.Oidc.SignInAsync(
            "Sam@north.example", "Passw0rd!", "north-advisory");
        login.AccessToken.ShouldNotBeNullOrEmpty();
    }
}
