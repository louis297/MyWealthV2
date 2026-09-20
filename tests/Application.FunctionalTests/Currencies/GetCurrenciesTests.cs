using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Currencies;

public class GetCurrenciesTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await FunctionalTestSetup.WebClient.GetAsync("/currencies");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns200()
    {
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var response = await SendAuthorized("/currencies", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task TenantAdmin_Returns200()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);

        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");
        var response = await SendAuthorized("/currencies", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Adviser_Returns200()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);

        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");
        var response = await SendAuthorized("/currencies", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Customer_Returns200()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);
        var response = await SendAuthorized("/currencies", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task DefaultList_ContainsSixSeedRowsWithIsEnabledSortedByCode()
    {
        var tokens = await SignInSystemAdmin();
        var response = await SendAuthorized("/currencies", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await ReadItems(response);
        items.Select(item => item.GetProperty("code").GetString()).ShouldBe([
            "AUD", "EUR", "GBP", "JPY", "NZD", "USD"
        ]);
        foreach (var item in items)
        {
            item.TryGetProperty("isEnabled", out _).ShouldBeTrue();
            item.TryGetProperty("name", out _).ShouldBeTrue();
            item.TryGetProperty("decimalPlaces", out _).ShouldBeTrue();
        }
    }

    [Test]
    public async Task EnabledOnlyTrue_OmitsDisabledRow()
    {
        await InsertDisabledXxx();
        var tokens = await SignInSystemAdmin();

        var enabled = await ReadItems(await SendAuthorized("/currencies?enabledOnly=true", tokens.AccessToken));
        enabled.Select(item => item.GetProperty("code").GetString()).ShouldNotContain("XXX");

        var all = await ReadItems(await SendAuthorized("/currencies?enabledOnly=false", tokens.AccessToken));
        all.Select(item => item.GetProperty("code").GetString()).ShouldContain("XXX");

        var omitted = await ReadItems(await SendAuthorized("/currencies", tokens.AccessToken));
        omitted.Select(item => item.GetProperty("code").GetString()).ShouldBe(
            all.Select(item => item.GetProperty("code").GetString()));
    }

    [Test]
    public async Task IllegalEnabledOnly_Returns400()
    {
        var tokens = await SignInSystemAdmin();
        var response = await SendAuthorized("/currencies?enabledOnly=maybe", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<TokenResponse> SignInSystemAdmin()
    {
        return await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
    }

    private static async Task InsertDisabledXxx()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [Currencies] ([Code], [Name], [DecimalPlaces], [IsActive])
            VALUES ('XXX', N'Test Disabled', 2, 0)
            """);
        await scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>().ReloadAsync();
    }

    private static async Task<HttpResponseMessage> SendAuthorized(string path, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await FunctionalTestSetup.WebClient.SendAsync(request);
    }

    private static async Task<List<JsonElement>> ReadItems(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
