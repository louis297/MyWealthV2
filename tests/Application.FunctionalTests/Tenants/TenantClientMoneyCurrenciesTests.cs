using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

/// <summary>Client money accounts R23: <c>clientMoneyCurrencies</c> on the tenant item.</summary>
[Parallelizable]
public class TenantClientMoneyCurrenciesTests : TestBase
{
    [Test]
    public async Task WithoutRows_IsEmpty_OnEveryTenantRead()
    {
        var tenant = await TestApp.CreateTenantAsync("Money None", "money-none");
        var system = await TenantHttp.SignInSystemAdmin();

        (await ReadByIdAsync(system.AccessToken, tenant.PublicId)).ShouldBeEmpty();
        (await ReadFromListAsync(system.AccessToken, tenant.PublicId)).ShouldBeEmpty();
        (await ReadAsync($"/tenants/by-code/{tenant.Code}", system.AccessToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task ActiveRows_AreListedSortedAToZ_AndADisabledRowIsNot()
    {
        var tenant = await TestApp.CreateTenantAsync("Money Sorted", "money-sorted");
        var other = await TestApp.CreateTenantAsync("Money Other", "money-other");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("USD"));
        await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("AUD"));
        var gbp = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("GBP"));
        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, gbp, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("NZD"));
        await ClientMoneyHttp.CreateAsync(token, other.PublicId, ClientMoneyHttp.Body("EUR"));

        (await ReadByIdAsync(token, tenant.PublicId)).ShouldBe(["AUD", "NZD", "USD"]);
        (await ReadFromListAsync(token, tenant.PublicId)).ShouldBe(["AUD", "NZD", "USD"]);
        (await ReadAsync($"/tenants/by-code/{tenant.Code}", token)).ShouldBe(["AUD", "NZD", "USD"]);
        (await ReadByIdAsync(token, other.PublicId)).ShouldBe(["EUR"]);
    }

    [TestCase(UserRole.TenantAdmin)]
    [TestCase(UserRole.Adviser)]
    public async Task ByCode_OwnFirm_CarriesTheCurrencies(UserRole role)
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var system = await TenantHttp.SignInSystemAdmin();
        await ClientMoneyHttp.CreateAsync(system.AccessToken, tenant.PublicId, ClientMoneyHttp.Body("USD"));
        await ClientMoneyHttp.CreateAsync(system.AccessToken, tenant.PublicId, ClientMoneyHttp.Body("NZD"));
        await TestApp.CreatePersonAsync(role, "Tess", "tess@north", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@north", "Password1!", "north-advisory");

        (await ReadAsync("/tenants/by-code/north-advisory", tokens.AccessToken)).ShouldBe(["NZD", "USD"]);
    }

    [Test]
    public async Task Item_CarriesNoBankDetails()
    {
        var tenant = await TestApp.CreateTenantAsync("Money Details", "money-details");
        var system = await TenantHttp.SignInSystemAdmin();
        await ClientMoneyHttp.CreateAsync(system.AccessToken, tenant.PublicId, ClientMoneyHttp.Body("USD", "SECRET-NUMBER-1"));

        var response = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}", system.AccessToken);

        var content = await response.Content.ReadAsStringAsync();
        content.ShouldNotContain("SECRET-NUMBER-1");
        content.ShouldNotContain("Simulator Bank");
    }

    private static Task<string[]> ReadByIdAsync(string token, Guid tenantId) =>
        ReadAsync($"/tenants/{tenantId}", token);

    private static async Task<string[]> ReadAsync(string path, string token)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, path, token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return Currencies(json.RootElement);
    }

    private static async Task<string[]> ReadFromListAsync(string token, Guid tenantId)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, "/tenants?pageSize=100", token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == tenantId);
        return Currencies(item);
    }

    private static string[] Currencies(JsonElement item) =>
        item.GetProperty("clientMoneyCurrencies").EnumerateArray().Select(code => code.GetString()!).ToArray();
}
