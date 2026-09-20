using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

public class CreateInstrumentTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken: null, ValidBody());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
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

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_CreatesAndGetMatchesNormalisedValues()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInAdviser();

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            symbol = " vti ",
            name = "  Vanguard Total Stock Market ETF  ",
            quoteCurrency = "usd"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var get = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{id}", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var item = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        item.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        item.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        item.RootElement.GetProperty("symbol").GetString().ShouldBe("VTI");
        item.RootElement.GetProperty("name").GetString().ShouldBe("Vanguard Total Stock Market ETF");
        item.RootElement.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        item.RootElement.GetProperty("isEnabled").GetBoolean().ShouldBeTrue();
        item.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        item.RootElement.TryGetProperty("price", out _).ShouldBeFalse();
    }

    [Test]
    public async Task TenantAdmin_Creates201()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task SystemAdmin_WithoutTenantId_Returns400()
    {
        var tokens = await InstrumentHttp.SignInSystemAdmin();

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemAdmin_WithTenantPublicId_Returns201()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await InstrumentHttp.SignInSystemAdmin();

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            symbol = "VTI",
            name = "Vanguard Total Stock Market ETF",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();
        var get = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{id}", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task SystemAdmin_UnknownTenant_Returns404()
    {
        var tokens = await InstrumentHttp.SignInSystemAdmin();

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            tenantId = Guid.NewGuid(),
            symbol = "VTI",
            name = "Vanguard Total Stock Market ETF",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_TenantIdInBody_Returns400()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInTenantAdmin();

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            symbol = "VTI",
            name = "Vanguard Total Stock Market ETF",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Adviser_TenantIdInBody_Returns400()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInAdviser();

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            symbol = "VTI",
            name = "Vanguard Total Stock Market ETF",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DuplicateSymbol_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        (await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, ValidBody()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            symbol = "vti",
            name = "Other name",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DuplicateSymbolIncludingDisabled_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var created = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", tokens.AccessToken, ValidBody());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        await DisableInstrumentAsync(id);

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SameSymbolInTwoTenants_ReturnsTwo201s()
    {
        var (_, northTokens) = await InstrumentHttp.SignInAdviser(
            "North Advisory", "north-advisory", "ann@north.example");
        var (_, southTokens) = await InstrumentHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example");

        var first = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", northTokens.AccessToken, ValidBody());
        var second = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", southTokens.AccessToken, ValidBody());

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task DisabledQuoteCurrency_Returns400()
    {
        await InsertDisabledXxx();
        var (_, tokens) = await InstrumentHttp.SignInAdviser();

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            symbol = "VTI",
            name = "Vanguard Total Stock Market ETF",
            quoteCurrency = "XXX"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledTenant_Returns400DisabledTarget()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInAdviser();
        await DisableTenantAsync(tenant.Id);

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, "/instruments", tokens.AccessToken, ValidBody());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(tenant.PublicId);
    }

    [Test]
    public async Task InvalidSymbol_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();

        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            symbol = "VT I",
            name = "Vanguard",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static object ValidBody() => new
    {
        symbol = "VTI",
        name = "Vanguard Total Stock Market ETF",
        quoteCurrency = "USD"
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

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }

    private static async Task DisableInstrumentAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == publicId);
        instrument.Disable();
        await db.SaveChangesAsync();
    }
}
