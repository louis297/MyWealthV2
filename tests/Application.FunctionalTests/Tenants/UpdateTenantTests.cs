using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

public class UpdateTenantTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", accessToken: null, new
        {
            name = "North Advisory Ltd",
            rowVersion = "x"
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

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "Firm T Ltd",
            rowVersion = "x"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "Firm A Ltd",
            rowVersion = "x"
        });
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

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "Firm C Ltd",
            rowVersion = "x"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SystemAdmin_Renames_Returns204()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantHttp.SignInSystemAdmin();
        var rowVersion = await ReadRowVersion(tenant.PublicId, tokens.AccessToken);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "North Advisory Ltd",
            rowVersion
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}", tokens.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("name").GetString().ShouldBe("North Advisory Ltd");
        json.RootElement.GetProperty("code").GetString().ShouldBe("north-advisory");
        json.RootElement.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task PutSameHistoricalDisabledCurrency_Returns204()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await DisableCurrency("NZD");
        var tokens = await TenantHttp.SignInSystemAdmin();
        var rowVersion = await ReadRowVersion(tenant.PublicId, tokens.AccessToken);

        var omitted = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "North Advisory",
            rowVersion
        });
        omitted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        rowVersion = await ReadRowVersion(tenant.PublicId, tokens.AccessToken);
        var equal = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "North Advisory",
            reportingCurrency = "NZD",
            rowVersion
        });
        equal.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}", tokens.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task PutNewDisabledCurrency_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await InsertDisabledXxx();
        var tokens = await TenantHttp.SignInSystemAdmin();
        var rowVersion = await ReadRowVersion(tenant.PublicId, tokens.AccessToken);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "North Advisory",
            reportingCurrency = "XXX",
            rowVersion
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithCode_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantHttp.SignInSystemAdmin();
        var rowVersion = await ReadRowVersion(tenant.PublicId, tokens.AccessToken);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "North Advisory Ltd",
            code = "other-code",
            rowVersion
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "North Advisory Ltd",
            rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", tokens.AccessToken, new
        {
            name = "North Advisory Ltd"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{Guid.NewGuid()}", tokens.AccessToken, new
        {
            name = "Ghost",
            rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> ReadRowVersion(Guid id, string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task DisableCurrency(string code)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [Currencies] SET [IsEnabled] = 0 WHERE [Code] = {0}", code);
        await scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>().ReloadAsync();
    }

    private static async Task InsertDisabledXxx()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [Currencies] ([Code], [Name], [DecimalPlaces], [IsEnabled])
            VALUES ('XXX', N'Test Disabled', 2, 0)
            """);
        await scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>().ReloadAsync();
    }
}
