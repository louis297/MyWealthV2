using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Users;

public class GetCurrentUserTests : TestBase
{
    [Test]
    public async Task GetMe_WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await FunctionalTestSetup.WebClient.GetAsync("/users/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_CanReadProfile()
    {
        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var response = await SendAuthorized(HttpMethod.Get, "/users/me", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("email").GetString().ShouldBe(DevelopmentIdentitySeeder.SystemAdminEmail);
        json.RootElement.GetProperty("role").GetString().ShouldBe("systemAdmin");
        json.RootElement.GetProperty("tenantId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task DisabledPerson_WithValidAccessToken_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        var person = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin,
            "Alice",
            "alice@firma",
            "Password1!",
            tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("alice@firma", "Password1!", tenant.Code);
        (await SendAuthorized(HttpMethod.Get, "/users/me", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await DisablePersonAsync(person.Id);

        (await SendAuthorized(HttpMethod.Get, "/users/me", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DisabledSystemAdmin_WithValidAccessToken_Returns403()
    {
        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
        int systemAdminId;
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            systemAdminId = await db.DomainUsers
                .Where(row => row.Email == DevelopmentIdentitySeeder.SystemAdminEmail)
                .Select(row => row.Id)
                .SingleAsync();
        }

        await DisablePersonAsync(systemAdminId);

        (await SendAuthorized(HttpMethod.Get, "/users/me", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAuthorized(HttpMethod.Get, "/tenants", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CrossTenantLogin_IsRejected()
    {
        var tenantA = await TestApp.CreateTenantAsync("Firm A", "firma");
        var tenantB = await TestApp.CreateTenantAsync("Firm B", "firmb");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin,
            "Alice",
            "alice@firma",
            "Password1!",
            tenantA.Id);

        var failed = await FunctionalTestSetup.Oidc.LoginFailsAsync("alice@firma", "Password1!", tenantB.Code);
        failed.ShouldBeTrue();
    }

    [Test]
    public async Task PasswordChange_LeavesTheRefreshToken()
    {
        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var me = await SendAuthorized(HttpMethod.Get, "/users/me", tokens.AccessToken);
        using var profile = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var rowVersion = profile.RootElement.GetProperty("rowVersion").GetString();

        var change = await SendAuthorized(HttpMethod.Put, "/users/me/password", tokens.AccessToken, new
        {
            currentPassword = DevelopmentIdentitySeeder.SystemAdminPassword,
            newPassword = "Administrator2!"
        });
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refresh = await FunctionalTestSetup.BackOfficeOidc.RefreshAsync(tokens.RefreshToken!);
        refresh.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Test]
    public async Task PutMe_CannotChangeEmail()
    {
        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
        var me = await SendAuthorized(HttpMethod.Get, "/users/me", tokens.AccessToken);
        using var profile = JsonDocument.Parse(await me.Content.ReadAsStringAsync());

        var response = await SendAuthorized(HttpMethod.Put, "/users/me", tokens.AccessToken, new
        {
            name = "Ada",
            rowVersion = profile.RootElement.GetProperty("rowVersion").GetString(),
            email = "other@localhost"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutMe_StaleRowVersion_Returns409()
    {
        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var response = await SendAuthorized(HttpMethod.Put, "/users/me", tokens.AccessToken, new
        {
            name = "Ada",
            rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Customer_AdviserPortalLogin_DoesNotIssueTokenForMe()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);

        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync("dee@firmc", "Password1!", "firmc");
        login.Code.ShouldBeNull();

        var response = await FunctionalTestSetup.WebClient.GetAsync("/users/me");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    private static async Task DisablePersonAsync(int userId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.DomainUsers.SingleAsync(row => row.Id == userId);
        user.Disable();
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> SendAuthorized(
        HttpMethod method,
        string path,
        string accessToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await FunctionalTestSetup.WebClient.SendAsync(request);
    }
}
