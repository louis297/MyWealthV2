using System.Net;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Users.TenantAdmins;

public class CreateTenantAdminTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", accessToken: null, ValidBody(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TenantAdmin_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");

        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("dee@firmc", "Password1!", "firmc");

        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static object ValidBody(Guid tenantId) => new
    {
        tenantId,
        name = "Alex Chen",
        email = "alex@north.example",
        password = "Passw0rd!"
    };
}
