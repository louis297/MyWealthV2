using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Users.TenantAdmins;

public class CreateTenantAdminLoginTests : TestBase
{
    [Test]
    public async Task HostedLogin_AfterCreate_CompletesWithTenantCodeEmailAndPassword()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();

        var create = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, new
            {
                tenantId = tenant.PublicId,
                name = "Alex Chen",
                email = "Alex@north.example",
                password = "Passw0rd!"
            });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        created.RootElement.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);

        var login = await FunctionalTestSetup.Oidc.SignInAsync(
            "Alex@north.example", "Passw0rd!", "north-advisory");
        login.AccessToken.ShouldNotBeNullOrEmpty();
    }
}
