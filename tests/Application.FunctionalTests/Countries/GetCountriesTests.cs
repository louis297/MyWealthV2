using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Countries;

/// <summary>Instrument-tax-attributes R1, R6: <c>GET /countries</c>.</summary>
[Parallelizable]
public class GetCountriesTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await FunctionalTestSetup.WebClient.GetAsync("/countries");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_GetsTheList()
    {
        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail, DevelopmentIdentitySeeder.SystemAdminPassword, tenantCode: null);

        await ShouldGetTheList(tokens.AccessToken);
    }

    [Test]
    public async Task TenantAdmin_GetsTheList()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");

        await ShouldGetTheList(tokens.AccessToken);
    }

    [Test]
    public async Task Adviser_GetsTheList()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        await ShouldGetTheList(tokens.AccessToken);
    }

    [Test]
    public async Task Customer_GetsTheList()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        await ShouldGetTheList(tokens.AccessToken);
    }

    /// <summary>Every code once as <c>{ code, name }</c>, sorted by name, without paging.</summary>
    private static async Task ShouldGetTheList(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/countries");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await FunctionalTestSetup.WebClient.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        var items = json.RootElement.EnumerateArray().ToList();
        items.ShouldAllBe(item => item.EnumerateObject().Select(property => property.Name).SequenceEqual(new[] { "code", "name" }));
        var countries = items.Select(item => (Code: item.GetProperty("code").GetString()!, Name: item.GetProperty("name").GetString()!)).ToList();
        countries.Count.ShouldBe(249);
        countries.Select(country => country.Code).Distinct().Count().ShouldBe(249);
        countries.ShouldContain(("NZ", "New Zealand"));
        countries.ShouldContain(("AU", "Australia"));
        countries.Select(country => country.Name)
            .ShouldBe(countries.Select(country => country.Name).OrderBy(name => name, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: false)));
    }
}
