using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class ClientAllowListTests : TestBase
{
    [Test]
    public async Task Customer_CorrectPassword_OnAdviserPortal_IssuesNoCode()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);

        (await TestApp.CheckPasswordAsync("dee@firmc", "Password1!", "firmc")).ShouldBeTrue();

        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync("dee@firmc", "Password1!", "firmc");

        login.Code.ShouldBeNull();
        FailureCopy(login.Body).ShouldBe("Invalid login.");

        var me = await FunctionalTestSetup.WebClient.GetAsync("/users/me");
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        me.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_CorrectPassword_OnAdviserPortal_IssuesNoCode()
    {
        (await TestApp.CheckPasswordAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null)).ShouldBeTrue();

        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        login.Code.ShouldBeNull();
        FailureCopy(login.Body).ShouldBe("Invalid login.");
        AssertNoRoleLeak(login.Body);
    }

    [Test]
    public async Task SystemAdmin_CorrectPassword_OnBackOffice_StillIssuesACode()
    {
        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task CustomerFailureCopy_MatchesTenantAdminWrongPassword()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmc", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);

        var customer = await FunctionalTestSetup.Oidc.SubmitLoginAsync("dee@firmc", "Password1!", "firmc");
        var wrongPassword = await FunctionalTestSetup.Oidc.SubmitLoginAsync("tess@firmc", "WrongPass1!", "firmc");

        FailureCopy(customer.Body).ShouldBe(FailureCopy(wrongPassword.Body));
        FailureCopy(customer.Body).ShouldBe("Invalid login.");
        AssertNoRoleLeak(customer.Body);
        AssertNoRoleLeak(wrongPassword.Body);
    }

    [Test]
    public async Task RegisteredClients_IncludeAdviserPortalBackOfficeAndCustomerPortal()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        (await manager.FindByClientIdAsync("adviser-portal")).ShouldNotBeNull();
        (await manager.FindByClientIdAsync(Services.BackOffice)).ShouldNotBeNull();

        var customer = await manager.FindByClientIdAsync("customer-portal");
        customer.ShouldNotBeNull();
        (await manager.ValidateClientSecretAsync(customer, CustomerPortalTestSecret.Value)).ShouldBeTrue();
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, customer);
        descriptor.ClientType.ShouldBe(ClientTypes.Confidential);
        descriptor.RedirectUris.Select(uri => uri.AbsoluteUri).ShouldBe(["https://localhost/signin-oidc"]);
        descriptor.PostLogoutRedirectUris.Select(uri => uri.AbsoluteUri).ShouldBe(["https://localhost/"]);
    }

    private static string FailureCopy(string html)
    {
        var match = Regex.Match(html, @"<p>([^<]+)</p>");
        return match.Success ? match.Groups[1].Value : html;
    }

    private static void AssertNoRoleLeak(string html)
    {
        html.ShouldNotContain("no identity");
        html.ShouldNotContain("wrong role");
        html.ShouldNotContain("customer cannot use this app");
    }
}
