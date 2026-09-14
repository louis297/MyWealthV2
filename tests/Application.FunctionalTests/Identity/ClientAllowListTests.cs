using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using OpenIddict.Abstractions;

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
    public async Task ReservedClients_AreNotRegistered()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        (await manager.FindByClientIdAsync("adviser-portal")).ShouldNotBeNull();
        (await manager.FindByClientIdAsync("customer-portal")).ShouldBeNull();
        (await manager.FindByClientIdAsync("back-office")).ShouldBeNull();
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
