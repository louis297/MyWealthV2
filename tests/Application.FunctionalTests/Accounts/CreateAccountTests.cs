using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class CreateAccountTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", accessToken: null, ValidBody(Guid.NewGuid()));

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

        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(customer.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_CreatesAssignedCustomerAndGetMatches()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "  Everyday spending  ",
            type = "Bank",
            currency = "nzd"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var item = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        item.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        item.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        item.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customer.PublicId);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Everyday spending");
        item.RootElement.GetProperty("type").GetString().ShouldBe("Bank");
        item.RootElement.GetProperty("currency").GetString().ShouldBe("NZD");
        item.RootElement.GetProperty("status").GetString().ShouldBe("Open");
        item.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        item.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        item.RootElement.TryGetProperty("balance", out _).ShouldBeFalse();
    }

    [Test]
    public async Task TenantAdmin_Creates201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();

        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(customer.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task SystemAdmin_WithoutTenantId_Returns400()
    {
        var (_, _, customer, _) = await AccountHttp.SignInAdviser();
        var tokens = await AccountHttp.SignInSystemAdmin();

        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(customer.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemAdmin_WithTenantPublicId_Returns201()
    {
        var (tenant, _, customer, _) = await AccountHttp.SignInAdviser();
        var tokens = await AccountHttp.SignInSystemAdmin();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task SystemAdmin_UnknownTenant_Returns404()
    {
        var (_, _, customer, _) = await AccountHttp.SignInAdviser();
        var tokens = await AccountHttp.SignInSystemAdmin();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = Guid.NewGuid(),
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_TenantIdInBody_Returns400()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Adviser_TenantIdInBody_Returns400()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Adviser_OtherAdvisersCustomer_Returns404()
    {
        var (tenant, _, _, tokens) = await AccountHttp.SignInAdviser();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, other.Id);

        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(otherCustomer.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PropertyOrCredit_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        (await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "House",
            type = "Property",
            currency = "NZD"
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "Card",
            type = "Credit",
            currency = "NZD"
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownType_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Vault",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DuplicateNamesUnderOneCustomer_ReturnTwo201s()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var first = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(customer.PublicId));
        var second = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(customer.PublicId));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task DisabledQuoteCurrency_Returns400()
    {
        await InsertDisabledXxx();
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "XXX"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledTenant_Returns400DisabledTarget()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        await DisableTenantAsync(tenant.Id);

        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(customer.PublicId));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(tenant.PublicId);
    }

    [Test]
    public async Task DisabledCustomer_Returns400DisabledUser()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        await DisableCustomerAsync(customer.Id);

        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(customer.PublicId));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(customer.PublicId);
    }

    [Test]
    public async Task WritableStatusOrIsActive_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        (await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD",
            status = "Closed"
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD",
            isActive = false
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownCustomer_Returns404()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(
            HttpMethod.Post, "/accounts", tokens.AccessToken, ValidBody(Guid.NewGuid()));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static object ValidBody(Guid customerId) => new
    {
        customerId,
        name = "Everyday spending",
        type = "Bank",
        currency = "NZD"
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

    private static async Task DisableCustomerAsync(int customerId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.Id == customerId);
        person.Disable();
        await db.SaveChangesAsync();
    }
}
