using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

public class GetTenantByCodeTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Get, "/tenants/by-code/north-advisory", accessToken: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_OwnCode_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@north", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@north", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        var response = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/north-advisory", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_OwnCode_AnyCase_ReturnsItem()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@north", "Password1!", "north-advisory");

        var lower = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/north-advisory", tokens.AccessToken);
        lower.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertItem(await ReadJson(lower), tenant.PublicId, "North Advisory", "north-advisory");

        var mixed = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/NORTH-ADVISORY", tokens.AccessToken);
        mixed.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertItem(await ReadJson(mixed), tenant.PublicId, "North Advisory", "north-advisory");
    }

    [Test]
    public async Task TenantAdmin_OtherTenantCode_Returns404()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreateTenantAsync("South Co", "south-co");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@north", "Password1!", "north-advisory");

        var response = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/south-co", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_UnknownCode_Returns404()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@north", "Password1!", "north-advisory");

        var response = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/does-not-exist", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Adviser_OwnCode_ReturnsItem_OtherAndUnknown_Return404()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreateTenantAsync("South Co", "south-co");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@north", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@north", "Password1!", "north-advisory");

        var own = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/north-advisory", tokens.AccessToken);
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertItem(await ReadJson(own), tenant.PublicId, "North Advisory", "north-advisory");

        (await TenantHttp.Send(HttpMethod.Get, "/tenants/by-code/south-co", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Get, "/tenants/by-code/does-not-exist", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_ExistingCode_Returns200_Unknown_Returns404()
    {
        var north = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var south = await TestApp.CreateTenantAsync("South Co", "south-co");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var northResponse = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/north-advisory", tokens.AccessToken);
        northResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertItem(await ReadJson(northResponse), north.PublicId, "North Advisory", "north-advisory");

        var southResponse = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/south-co", tokens.AccessToken);
        southResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertItem(await ReadJson(southResponse), south.PublicId, "South Co", "south-co");

        (await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/does-not-exist", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_Adviser_Customer_ManageVerbs_Return403()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@north", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@north", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@north", "Password1!", tenant.Id, adviser.Id);

        var tenantAdmin = await FunctionalTestSetup.Oidc.SignInAsync("tess@north", "Password1!", "north-advisory");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@north", "Password1!", "north-advisory");
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        await AssertManageForbidden(tenant.PublicId, tenantAdmin.AccessToken);
        await AssertManageForbidden(tenant.PublicId, adviserTokens.AccessToken);
        await AssertManageForbidden(tenant.PublicId, customerTokens.AccessToken);
    }

    [Test]
    public async Task PublicId_StillHitsGetById_CodePathDoesNotBindAsId()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north", "Password1!", tenant.Id);
        var tenantAdmin = await FunctionalTestSetup.Oidc.SignInAsync("tess@north", "Password1!", "north-advisory");
        var systemAdmin = await TenantHttp.SignInSystemAdmin();

        (await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{tenant.PublicId}", tenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var byCode = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/by-code/north-advisory", tenantAdmin.AccessToken);
        byCode.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertItem(await ReadJson(byCode), tenant.PublicId, "North Advisory", "north-advisory");

        var asId = await TenantHttp.Send(
            HttpMethod.Get, "/tenants/north-advisory", systemAdmin.AccessToken);
        asId.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Test]
    public void ByCode_EndpointMetadata_RequiresTenantsRead()
    {
        var endpoint = FunctionalTestSetup.WebApiServices.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(route =>
                route.RoutePattern.RawText?.Contains("by-code", StringComparison.Ordinal) == true
                && route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
        var authorize = endpoint.Metadata.GetMetadata<IAuthorizeData>();
        authorize.ShouldNotBeNull();
        authorize.Policy.ShouldBe(Policies.TenantsRead);
        authorize.Policy.ShouldNotBe(Policies.TenantsManage);
        string.IsNullOrWhiteSpace(authorize.Policy).ShouldBeFalse();
    }

    [Test]
    public async Task TwoTenantAdmins_CannotReadEachOthersCode_ListsStayForbidden()
    {
        var tenantA = await TestApp.CreateTenantAsync("Firm A", "firma");
        var tenantB = await TestApp.CreateTenantAsync("Firm B", "firmb");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Alex", "alex@a", "Password1!", tenantA.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Blair", "blair@b", "Password1!", tenantB.Id);
        var adminA = await FunctionalTestSetup.Oidc.SignInAsync("alex@a", "Password1!", "firma");
        var adminB = await FunctionalTestSetup.Oidc.SignInAsync("blair@b", "Password1!", "firmb");

        (await TenantHttp.Send(HttpMethod.Get, "/tenants/by-code/firmb", adminA.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Get, "/tenants/by-code/firma", adminB.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await TenantHttp.Send(HttpMethod.Get, "/tenants", adminA.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, "/tenants", adminB.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task AssertManageForbidden(Guid tenantId, string accessToken)
    {
        (await TenantHttp.Send(HttpMethod.Get, "/tenants", accessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenantId}", accessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Post, "/tenants", accessToken, new
            {
                name = "Other Firm",
                code = "other-firm",
                reportingCurrency = "NZD"
            }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenantId}", accessToken, new
            {
                name = "Renamed",
                rowVersion = "x"
            }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenantId}/disable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenantId}/enable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static void AssertItem(JsonDocument json, Guid id, string name, string code)
    {
        json.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        json.RootElement.GetProperty("name").GetString().ShouldBe(name);
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
        json.RootElement.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
        json.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        json.RootElement.TryGetProperty("created", out _).ShouldBeTrue();
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
