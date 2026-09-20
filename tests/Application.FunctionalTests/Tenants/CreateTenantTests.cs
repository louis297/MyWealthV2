using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

public class CreateTenantTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", accessToken: null, new
        {
            name = "North Advisory",
            code = "North-Advisory",
            reportingCurrency = "nzd"
        });

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

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SystemAdmin_CreatesAndGetMatchesNormalisedValues()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var usersBefore = await TestApp.CountAsync<ApplicationUser>();
        var peopleBefore = await TestApp.CountAsync<Domain.Entities.User>();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, new
        {
            name = "North Advisory",
            code = "North-Advisory",
            reportingCurrency = "nzd"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var get = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{id}", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var item = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        item.RootElement.GetProperty("name").GetString().ShouldBe("North Advisory");
        item.RootElement.GetProperty("code").GetString().ShouldBe("north-advisory");
        item.RootElement.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
        item.RootElement.GetProperty("isEnabled").GetBoolean().ShouldBeTrue();

        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(usersBefore);
        (await TestApp.CountAsync<Domain.Entities.User>()).ShouldBe(peopleBefore);
    }

    [Test]
    public async Task DuplicateNameDifferentCase_Returns400()
    {
        await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, new
        {
            name = "north advisory",
            code = "other-code",
            reportingCurrency = "NZD"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DuplicateCodeDifferentCase_Returns400()
    {
        await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, new
        {
            name = "Other Firm",
            code = "North-Advisory",
            reportingCurrency = "NZD"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownCurrency_Returns400()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, new
        {
            name = "North Advisory",
            code = "north-advisory",
            reportingCurrency = "ZZZ"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledCurrency_Returns400()
    {
        await InsertDisabledXxx();
        var tokens = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, new
        {
            name = "North Advisory",
            code = "north-advisory",
            reportingCurrency = "XXX"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static object ValidBody() => new
    {
        name = "North Advisory",
        code = "north-advisory",
        reportingCurrency = "NZD"
    };

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
}
