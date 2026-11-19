using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

[Parallelizable]
public class PerClientCookieTests : TestBase
{
    private const string AdviserPortal = "adviser-portal";
    private const string BackOffice = "back-office";
    private const string CustomerPortal = "customer-portal";
    private const string AdviserRedirect = "https://localhost/signin-oidc";
    private const string AdviserPostLogout = "https://localhost/";
    private const string BackOfficeRedirect = "https://localhost/back-office/signin-oidc";
    private const string AdviserPortalCookie = ".AspNetCore.Identity.AdviserPortal";
    private const string BackOfficeCookie = ".AspNetCore.Identity.BackOffice";
    private const string CustomerPortalCookie = ".AspNetCore.Identity.CustomerPortal";

    [Test]
    public async Task SystemAdminOnBackOffice_DoesNotSilentlyAuthorizeAdviserPortal()
    {
        using var browser = new IdentityBrowser();
        await browser.LoginAsync(
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.IsLogin.ShouldBeTrue($"Adviser Portal authorize completed silently: {adviser.Location}");
        adviser.Code.ShouldBeNull();

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.Code.ShouldNotBeNullOrWhiteSpace();
        backOffice.IsLogin.ShouldBeFalse();
    }

    [Test]
    public async Task TenantAdminOnAdviserPortal_DoesNotAuthorizeBackOffice_AndKeepsPortalSession()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Cookies", "firm-cookies");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-cookies", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "tess@firm-cookies", "Password1!", "firm-cookies");

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.IsLogin.ShouldBeTrue($"Back Office authorize completed silently: {backOffice.Location}");
        backOffice.Code.ShouldBeNull();

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.Code.ShouldNotBeNullOrWhiteSpace();
        adviser.IsLogin.ShouldBeFalse();
    }

    [Test]
    public async Task BackOfficeLogin_DoesNotReplaceAdviserPortalSubject()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Isolate", "firm-isolate");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-isolate", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "tess@firm-isolate", "Password1!", "firm-isolate");

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.IsLogin.ShouldBeTrue();
        var backOfficeCode = await browser.SubmitLoginAsync(
            backOffice.Location,
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
        backOfficeCode.ShouldNotBeNullOrWhiteSpace();

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.Code.ShouldNotBeNullOrWhiteSpace();
        (await browser.EmailForAsync(AdviserPortal, adviser)).ShouldBe("tess@firm-isolate");
    }

    [Test]
    public async Task AdviserPortalLogout_ClearsOnlyThatIdentityCookie()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Logout", "firm-logout");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-logout", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "tess@firm-logout", "Password1!", "firm-logout");
        await browser.LoginAsync(
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var logout = await browser.LogoutAsync(AdviserPortal, AdviserPostLogout);
        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        logout.Headers.Location.ShouldNotBeNull();
        logout.Headers.Location.ToString().ShouldBe(AdviserPostLogout);

        var setCookies = logout.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join('\n', values)
            : "";
        setCookies.ShouldContain(AdviserPortalCookie);
        setCookies.ShouldNotContain(BackOfficeCookie);

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.IsLogin.ShouldBeTrue();
        adviser.Code.ShouldBeNull();

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.Code.ShouldNotBeNullOrWhiteSpace();
        (await browser.EmailForAsync(BackOffice, backOffice)).ShouldBe(DevelopmentIdentitySeeder.SystemAdminEmail);
    }

    [Test]
    public async Task Customer_OnCustomerPortal_ReceivesACode()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Customer", "firm-customer");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-customer", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-customer", "Password1!", tenant.Id, adviser.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(CustomerPortal, "cee@firm-customer", "Password1!", "firm-customer");
    }

    [Test]
    public async Task Staff_OnCustomerPortal_IssuesNoCode()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Staff", "firm-staff");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-staff", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        var started = await browser.AuthorizeAsync(CustomerPortal);
        started.IsLogin.ShouldBeTrue();
        var code = await browser.SubmitLoginAsync(
            started.Location, CustomerPortal, "tess@firm-staff", "Password1!", "firm-staff");
        code.ShouldBeNull();
    }

    [Test]
    public async Task CustomerOnCustomerPortal_DoesNotSilentlyAuthorizeAdviserPortal()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Cross", "firm-cross");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-cross", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-cross", "Password1!", tenant.Id, adviser.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(CustomerPortal, "cee@firm-cross", "Password1!", "firm-cross");

        var adviserPortal = await browser.AuthorizeAsync(AdviserPortal);
        adviserPortal.IsLogin.ShouldBeTrue($"Adviser Portal authorize completed silently: {adviserPortal.Location}");
        adviserPortal.Code.ShouldBeNull();
    }

    [Test]
    public async Task CustomerPortalLogout_ClearsOnlyThatIdentityCookie()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Both", "firm-both");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-both", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-both", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-both", "Password1!", tenant.Id, adviser.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "tess@firm-both", "Password1!", "firm-both");
        await browser.LoginAsync(CustomerPortal, "cee@firm-both", "Password1!", "firm-both");

        var logout = await browser.LogoutAsync(CustomerPortal, AdviserPostLogout);
        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        logout.Headers.Location.ShouldNotBeNull();
        logout.Headers.Location.ToString().ShouldBe(AdviserPostLogout);

        var setCookies = logout.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join('\n', values)
            : "";
        setCookies.ShouldContain(CustomerPortalCookie);
        setCookies.ShouldNotContain(AdviserPortalCookie);

        var customer = await browser.AuthorizeAsync(CustomerPortal);
        customer.IsLogin.ShouldBeTrue();
        customer.Code.ShouldBeNull();

        var adviserPortal = await browser.AuthorizeAsync(AdviserPortal);
        adviserPortal.Code.ShouldNotBeNullOrWhiteSpace();
        (await browser.EmailForAsync(AdviserPortal, adviserPortal)).ShouldBe("tess@firm-both");
    }

    [Test]
    public async Task PasswordChange_RejectsTheAdviserPortalIdentityCookie()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Stamp", "firm-stamp");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-stamp", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        var login = await browser.LoginAsync(AdviserPortal, "ann@firm-stamp", "Password1!", "firm-stamp");
        await ChangePasswordAsync(await browser.AccessTokenForAsync(AdviserPortal, login), "Password1!", "Password2!");

        var after = await browser.AuthorizeAsync(AdviserPortal);
        after.IsLogin.ShouldBeTrue($"Adviser Portal authorize completed silently after a password change: {after.Location}");
        after.Code.ShouldBeNull();
        after.SetCookies.ShouldContain(AdviserPortalCookie);
    }

    [Test]
    public async Task PasswordChange_NewPasswordSignsInAgain()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Stamp New", "firm-stamp-new");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-stamp-new", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        var login = await browser.LoginAsync(AdviserPortal, "ann@firm-stamp-new", "Password1!", "firm-stamp-new");
        await ChangePasswordAsync(await browser.AccessTokenForAsync(AdviserPortal, login), "Password1!", "Password2!");

        var after = await browser.AuthorizeAsync(AdviserPortal);
        after.IsLogin.ShouldBeTrue($"Adviser Portal authorize completed silently after a password change: {after.Location}");
        var code = await browser.SubmitLoginAsync(
            after.Location, AdviserPortal, "ann@firm-stamp-new", "Password2!", "firm-stamp-new");
        code.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task UnchangedPassword_KeepsSilentAuthorize()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Stamp Same", "firm-stamp-same");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-stamp-same", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "ann@firm-stamp-same", "Password1!", "firm-stamp-same");

        var first = await browser.AuthorizeAsync(AdviserPortal);
        first.Code.ShouldNotBeNullOrWhiteSpace();
        var second = await browser.AuthorizeAsync(AdviserPortal);
        second.Code.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task PasswordChange_RejectsTheBackOfficeIdentityCookie()
    {
        using var browser = new IdentityBrowser();
        var login = await browser.LoginAsync(
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
        await ChangePasswordAsync(
            await browser.AccessTokenForAsync(BackOffice, login),
            DevelopmentIdentitySeeder.SystemAdminPassword,
            "Administrator2!");

        var after = await browser.AuthorizeAsync(BackOffice);
        after.IsLogin.ShouldBeTrue($"Back Office authorize completed silently after a password change: {after.Location}");
        after.Code.ShouldBeNull();
    }

    [Test]
    public async Task PasswordChange_RejectsTheCustomerPortalIdentityCookie()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Stamp Customer", "firm-stamp-cust");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-stamp-cust", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-stamp-cust", "Password1!", tenant.Id, adviser.Id);

        using var browser = new IdentityBrowser();
        var login = await browser.LoginAsync(CustomerPortal, "cee@firm-stamp-cust", "Password1!", "firm-stamp-cust");
        await ChangePasswordAsync(await browser.AccessTokenForAsync(CustomerPortal, login), "Password1!", "Password2!");

        var after = await browser.AuthorizeAsync(CustomerPortal);
        after.IsLogin.ShouldBeTrue($"Customer Portal authorize completed silently after a password change: {after.Location}");
        after.Code.ShouldBeNull();
    }

    private static async Task ChangePasswordAsync(string accessToken, string currentPassword, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/users/me/password");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { currentPassword, newPassword });
        var response = await FunctionalTestSetup.WebClient.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
}
