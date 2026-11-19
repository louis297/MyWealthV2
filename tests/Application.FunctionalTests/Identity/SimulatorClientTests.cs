using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MyWealthV2.Application.FunctionalTests.Identity;

/// <summary>Simulator spec R5–R8 (identity-auth A48–A50, A52, A53): the fourth client <c>simulator</c>.</summary>
[Parallelizable]
public class SimulatorClientTests : TestBase
{
    private const string Simulator = "simulator";
    private const string BackOffice = "back-office";
    private const string AdviserPortal = "adviser-portal";
    private const string CustomerPortal = "customer-portal";
    private const string SimulatorCookie = ".AspNetCore.Identity.Simulator";
    private const string BackOfficeCookie = ".AspNetCore.Identity.BackOffice";
    private const string SimulatorPostLogout = SimulatorTestSecret.Origin + "/";

    [Test]
    public async Task Operator_WithoutTenantCode_ReceivesACode()
    {
        using var browser = new IdentityBrowser();

        var hit = await browser.LoginAsync(
            Simulator,
            DevelopmentIdentitySeeder.SimulatorOperatorEmail,
            DevelopmentIdentitySeeder.SimulatorOperatorPassword,
            tenantCode: null);

        hit.Code.ShouldNotBeNullOrWhiteSpace();
        (await browser.EmailForAsync(Simulator, hit)).ShouldBe(DevelopmentIdentitySeeder.SimulatorOperatorEmail);
    }

    [Test]
    public async Task OperatorToken_HasNoApiScopeAndNoRefreshToken()
    {
        using var browser = new IdentityBrowser();
        var hit = await browser.LoginAsync(
            Simulator,
            DevelopmentIdentitySeeder.SimulatorOperatorEmail,
            DevelopmentIdentitySeeder.SimulatorOperatorPassword,
            tenantCode: null);

        using var response = JsonDocument.Parse(await browser.TokenResponseForAsync(Simulator, hit));

        response.RootElement.TryGetProperty("refresh_token", out _).ShouldBeFalse();
        using var payload = IdentityBrowser.Payload(response.RootElement.GetProperty("access_token").GetString()!);
        Scopes(payload).ShouldNotContain("api");
        Scopes(payload).ShouldNotContain(OpenIddictConstants.Scopes.OfflineAccess);
        payload.RootElement.GetProperty("role").GetString().ShouldBe("simulatorOperator");
    }

    [Test]
    public async Task Authorize_AskingForApi_IssuesNoCode()
    {
        using var client = FunctionalTestSetup.Identity.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/connect/authorize?" + string.Join("&",
        [
            "client_id=" + Simulator,
            "redirect_uri=" + Uri.EscapeDataString(IdentityBrowser.SimulatorRedirect),
            "response_type=code",
            "scope=" + Uri.EscapeDataString("openid profile email api"),
            "code_challenge=abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG",
            "code_challenge_method=S256",
            "state=xyz"
        ]));

        var location = response.Headers.Location?.ToString() ?? "";
        location.ShouldNotContain("code=");
        if (location.StartsWith(IdentityBrowser.SimulatorRedirect, StringComparison.OrdinalIgnoreCase))
        {
            location.ShouldContain("error=invalid_scope");
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Test]
    public async Task SystemAdmin_OnSimulator_GetsTheUniformFailure()
    {
        await AssertNoCodeOnSimulatorAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
    }

    [TestCase(UserRole.TenantAdmin)]
    [TestCase(UserRole.Adviser)]
    [TestCase(UserRole.Customer)]
    public async Task TenantPeople_OnSimulator_GetTheUniformFailure(UserRole role)
    {
        var code = "firm-sim-" + role.ToString().ToLowerInvariant();
        var tenant = await TestApp.CreateTenantAsync("Firm Sim", code);
        var email = $"person@{code}";
        if (role == UserRole.Customer)
        {
            var adviser = await TestApp.CreatePersonAsync(
                UserRole.Adviser, "Ann", $"adviser@{code}", "Password1!", tenant.Id);
            await TestApp.CreatePersonAsync(role, "Cee", email, "Password1!", tenant.Id, adviser.Id);
        }
        else
        {
            await TestApp.CreatePersonAsync(role, "Tess", email, "Password1!", tenant.Id);
        }

        await AssertNoCodeOnSimulatorAsync(email, "Password1!", code);
    }

    [TestCase(BackOffice)]
    [TestCase(AdviserPortal)]
    [TestCase(CustomerPortal)]
    public async Task Operator_OnOtherClients_GetsTheUniformFailure(string clientId)
    {
        using var browser = new IdentityBrowser();
        var started = await browser.AuthorizeAsync(clientId);
        started.IsLogin.ShouldBeTrue();

        var code = await browser.SubmitLoginAsync(
            started.Location,
            clientId,
            DevelopmentIdentitySeeder.SimulatorOperatorEmail,
            DevelopmentIdentitySeeder.SimulatorOperatorPassword,
            tenantCode: null);

        code.ShouldBeNull();
        FailureCopy(browser.LastLoginBody).ShouldBe("Invalid login.");
    }

    [Test]
    public async Task BackOfficeCookie_DoesNotAuthorizeSimulator()
    {
        using var browser = new IdentityBrowser();
        await browser.LoginAsync(
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var simulator = await browser.AuthorizeAsync(Simulator);

        simulator.IsLogin.ShouldBeTrue($"Simulator authorize completed silently: {simulator.Location}");
        simulator.Code.ShouldBeNull();
    }

    [Test]
    public async Task SimulatorLogout_ClearsOnlyThatIdentityCookie()
    {
        using var browser = new IdentityBrowser();
        await browser.LoginAsync(
            Simulator,
            DevelopmentIdentitySeeder.SimulatorOperatorEmail,
            DevelopmentIdentitySeeder.SimulatorOperatorPassword,
            tenantCode: null);
        await browser.LoginAsync(
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var logout = await browser.LogoutAsync(Simulator, SimulatorPostLogout);

        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        logout.Headers.Location?.ToString().ShouldBe(SimulatorPostLogout);
        var setCookies = logout.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join('\n', values) : "";
        setCookies.ShouldContain(SimulatorCookie);
        setCookies.ShouldNotContain(BackOfficeCookie);

        (await browser.AuthorizeAsync(Simulator)).IsLogin.ShouldBeTrue();
        (await browser.AuthorizeAsync(BackOffice)).Code.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task SimulatorClient_IsConfidentialWithItsOwnOriginAndNoRefreshOrApi()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var client = await manager.FindByClientIdAsync(Simulator);

        client.ShouldNotBeNull();
        (await manager.ValidateClientSecretAsync(client, SimulatorTestSecret.Value)).ShouldBeTrue();
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, client);
        descriptor.ClientType.ShouldBe(ClientTypes.Confidential);
        descriptor.RedirectUris.Select(uri => uri.AbsoluteUri).ShouldBe([IdentityBrowser.SimulatorRedirect]);
        descriptor.PostLogoutRedirectUris.Select(uri => uri.AbsoluteUri).ShouldBe([SimulatorPostLogout]);
        descriptor.Permissions.ShouldContain(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.ShouldNotContain(Permissions.GrantTypes.RefreshToken);
        descriptor.Permissions.ShouldNotContain(Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess);
        descriptor.Permissions.ShouldNotContain(Permissions.Prefixes.Scope + "api");
    }

    private static async Task AssertNoCodeOnSimulatorAsync(string email, string password, string? tenantCode)
    {
        using var browser = new IdentityBrowser();
        var started = await browser.AuthorizeAsync(Simulator);
        started.IsLogin.ShouldBeTrue($"Simulator authorize did not reach the hosted login: {started.Location}");

        var code = await browser.SubmitLoginAsync(started.Location, Simulator, email, password, tenantCode);

        code.ShouldBeNull();
        FailureCopy(browser.LastLoginBody).ShouldBe("Invalid login.");
    }

    private static IReadOnlyList<string> Scopes(JsonDocument payload)
    {
        if (!payload.RootElement.TryGetProperty("scope", out var scope))
        {
            return [];
        }

        return scope.ValueKind == JsonValueKind.Array
            ? scope.EnumerateArray().Select(item => item.GetString()!).ToList()
            : scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FailureCopy(string html)
    {
        var match = Regex.Match(html, @"<p>([^<]+)</p>");
        return match.Success ? match.Groups[1].Value : html;
    }
}
