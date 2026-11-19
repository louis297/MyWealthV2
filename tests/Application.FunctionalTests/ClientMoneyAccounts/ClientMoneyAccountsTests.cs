using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;

/// <summary>Client money account routes, R1–R10 (docs/features/client-money-accounts.md §4.1).</summary>
[Parallelizable]
public class ClientMoneyAccountsTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect_OnEveryRoute()
    {
        var tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var path = ClientMoneyHttp.Path(tenantId);

        foreach (var (method, route, body) in Routes(path, id))
        {
            var response = await TenantHttp.Send(method, route, accessToken: null, body);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task TenantAdminAdviserAndCustomer_Return403_OnEveryRoute()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Money Roles", "firm-money-roles");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-money-roles", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-money-roles", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-money-roles", "Password1!", tenant.Id, adviser.Id);
        var system = await TenantHttp.SignInSystemAdmin();
        var id = await ClientMoneyHttp.CreateAsync(system.AccessToken, tenant.PublicId, ClientMoneyHttp.Body());

        var tokens = new[]
        {
            (await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-money-roles", "Password1!", "firm-money-roles")).AccessToken,
            (await FunctionalTestSetup.Oidc.SignInAsync("ann@firm-money-roles", "Password1!", "firm-money-roles")).AccessToken,
            (await TestApp.IssueAccessTokenAsync(customer)).AccessToken
        };

        foreach (var token in tokens)
        {
            foreach (var (method, route, body) in Routes(ClientMoneyHttp.Path(tenant.PublicId), id))
            {
                (await TenantHttp.Send(method, route, token, body))
                    .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
            }
        }

        (await ClientMoneyHttp.CountAsync(tenant.Id)).ShouldBe(1);
    }

    [Test]
    public async Task UnknownTenant_Returns404()
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var path = ClientMoneyHttp.Path(Guid.NewGuid());

        (await TenantHttp.Send(HttpMethod.Get, path, system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Post, path, system.AccessToken, ClientMoneyHttp.Body()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Get, $"{path}/{Guid.NewGuid()}", system.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Create_Returns201WithLocation_AndGetReturnsTheFields()
    {
        var tenant = await TestApp.CreateTenantAsync("Money Create", "money-create");
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Post, ClientMoneyHttp.Path(tenant.PublicId), system.AccessToken, new
        {
            currency = "usd",
            bankName = "  Simulator Bank ",
            accountName = " North Advisory Ltd Client Funds Trust Account ",
            accountNumber = " 02-0100-0123456-00 "
        });

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        Guid id;
        using (var json = JsonDocument.Parse(content))
        {
            id = json.RootElement.GetProperty("id").GetGuid();
        }

        response.Headers.Location!.ToString().ShouldBe($"/tenants/{tenant.PublicId}/client-money-accounts/{id}");
        var item = await ClientMoneyHttp.GetAsync(system.AccessToken, tenant.PublicId, id);
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("bankName").GetString().ShouldBe("Simulator Bank");
        item.GetProperty("accountName").GetString().ShouldBe("North Advisory Ltd Client Funds Trust Account");
        item.GetProperty("accountNumber").GetString().ShouldBe("02-0100-0123456-00");
        item.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        item.GetProperty("created").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Test]
    public async Task List_SortsByCurrencyThenActiveThenNewest_AndFiltersEnabledOnly()
    {
        var tenant = await TestApp.CreateTenantAsync("Money List", "money-list");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var usdOld = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("USD"));
        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, usdOld, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var usdNewer = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("USD"));
        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, usdNewer, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var usdActive = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("USD"));
        var nzd = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("NZD"));
        var aud = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("AUD"));

        var all = await ListIdsAsync(token, ClientMoneyHttp.Path(tenant.PublicId));
        var active = await ListIdsAsync(token, $"{ClientMoneyHttp.Path(tenant.PublicId)}?enabledOnly=true");
        var explicitAll = await ListIdsAsync(token, $"{ClientMoneyHttp.Path(tenant.PublicId)}?enabledOnly=false");
        var paged = await ListIdsAsync(token, $"{ClientMoneyHttp.Path(tenant.PublicId)}?page=2&pageSize=2");

        all.ShouldBe([aud, nzd, usdActive, usdNewer, usdOld]);
        explicitAll.ShouldBe(all);
        active.ShouldBe([aud, nzd, usdActive]);
        paged.ShouldBe([usdActive, usdNewer]);
    }

    [Test]
    public async Task List_EnabledOnlyNotABoolean_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("Money Flag", "money-flag");
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(
            HttpMethod.Get, $"{ClientMoneyHttp.Path(tenant.PublicId)}?enabledOnly=maybe", system.AccessToken);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("EnabledOnly");
    }

    [TestCase("currency", null)]
    [TestCase("currency", "XXX")]
    [TestCase("bankName", "")]
    [TestCase("bankName", "   ")]
    [TestCase("bankName", Long101)]
    [TestCase("accountName", "")]
    [TestCase("accountName", Long141)]
    [TestCase("accountNumber", "")]
    [TestCase("accountNumber", "12345678901234567890123456789012345")]
    [TestCase("accountNumber", "02/0100")]
    [TestCase("accountNumber", "GB29  NWBK")]
    public async Task Create_FieldOutsideItsRule_Returns400AtThatField(string field, string? value)
    {
        var tenant = await TestApp.CreateTenantAsync("Money Fields", "money-fields");
        var system = await TenantHttp.SignInSystemAdmin();
        var body = new Dictionary<string, object?>
        {
            ["currency"] = "USD",
            ["bankName"] = "Simulator Bank",
            ["accountName"] = "Client Funds Trust Account",
            ["accountNumber"] = ClientMoneyHttp.UniqueNumber(),
            [field] = value
        };

        var response = await TenantHttp.Send(HttpMethod.Post, ClientMoneyHttp.Path(tenant.PublicId), system.AccessToken, body);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(field);
        (await ClientMoneyHttp.CountAsync(tenant.Id)).ShouldBe(0);
    }

    [TestCase("isActive", false)]
    [TestCase("tenantId", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    [TestCase("id", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    public async Task Create_BodyCarriesAServerField_Returns400(string field, object value)
    {
        var tenant = await TestApp.CreateTenantAsync("Money Server Field", "money-server-field");
        var system = await TenantHttp.SignInSystemAdmin();
        var body = new Dictionary<string, object?>
        {
            ["currency"] = "USD",
            ["bankName"] = "Simulator Bank",
            ["accountName"] = "Client Funds Trust Account",
            ["accountNumber"] = ClientMoneyHttp.UniqueNumber(),
            [field] = value
        };

        var response = await TenantHttp.Send(HttpMethod.Post, ClientMoneyHttp.Path(tenant.PublicId), system.AccessToken, body);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(field);
        (await ClientMoneyHttp.CountAsync(tenant.Id)).ShouldBe(0);
    }

    [Test]
    public async Task SecondActiveInTheCurrency_Returns400_ThenAfterDisableCreates_AndEnablingTheFirstReturns400()
    {
        var tenant = await TestApp.CreateTenantAsync("Money Per Currency", "money-per-currency");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var first = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("USD"));

        var second = await TenantHttp.Send(HttpMethod.Post, ClientMoneyHttp.Path(tenant.PublicId), token, ClientMoneyHttp.Body("USD"));
        (await CustodialHttp.ErrorsAsync(second))["Currency"]
            .ShouldBe(["The tenant already has an active client money account in USD."]);

        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, first, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body("USD"));

        var enable = await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, first, active: true);
        (await CustodialHttp.ErrorsAsync(enable))["Currency"]
            .ShouldBe(["The tenant already has an active client money account in USD."]);
        (await ClientMoneyHttp.GetAsync(token, tenant.PublicId, first)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task AccountNumberActiveInAnotherTenant_Returns400WithoutTheTenant_AndReuseAfterDisableCreates()
    {
        var north = await TestApp.CreateTenantAsync("North Money Number", "north-money-number");
        var south = await TestApp.CreateTenantAsync("South Money Number", "south-money-number");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var number = ClientMoneyHttp.UniqueNumber();
        var northRow = await ClientMoneyHttp.CreateAsync(token, north.PublicId, ClientMoneyHttp.Body("USD", number));

        var clash = await TenantHttp.Send(
            HttpMethod.Post, ClientMoneyHttp.Path(south.PublicId), token, ClientMoneyHttp.Body("NZD", number.ToLowerInvariant()));
        var errors = await CustodialHttp.ErrorsAsync(clash);
        errors["AccountNumber"].ShouldBe(["This account number is already in use by an active client money account."]);
        errors["AccountNumber"].Single().ShouldNotContain("North");

        (await ClientMoneyHttp.SetActiveAsync(token, north.PublicId, northRow, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ClientMoneyHttp.CreateAsync(token, south.PublicId, ClientMoneyHttp.Body("NZD", number));

        var enable = await ClientMoneyHttp.SetActiveAsync(token, north.PublicId, northRow, active: true);
        (await CustodialHttp.ErrorsAsync(enable))["AccountNumber"]
            .ShouldBe(["This account number is already in use by an active client money account."]);
    }

    [Test]
    public async Task DisableAndEnable_AreIdempotent_AndAlreadyInTheStateWritesNothing()
    {
        var tenant = await TestApp.CreateTenantAsync("Money Toggle", "money-toggle");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var id = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body());

        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, id, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterDisable = await ClientMoneyHttp.RowVersionAsync(token, tenant.PublicId, id);
        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, id, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ClientMoneyHttp.RowVersionAsync(token, tenant.PublicId, id)).ShouldBe(afterDisable);
        (await ClientMoneyHttp.GetAsync(token, tenant.PublicId, id)).GetProperty("isActive").GetBoolean().ShouldBeFalse();

        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, id, active: true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterEnable = await ClientMoneyHttp.RowVersionAsync(token, tenant.PublicId, id);
        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, id, active: true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ClientMoneyHttp.RowVersionAsync(token, tenant.PublicId, id)).ShouldBe(afterEnable);
        (await ClientMoneyHttp.GetAsync(token, tenant.PublicId, id)).GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [TestCase("disable")]
    [TestCase("enable")]
    public async Task MissingRowVersion_Returns400_AndStaleRowVersion_Returns409(string action)
    {
        var tenant = await TestApp.CreateTenantAsync("Money Version", "money-version");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var id = await ClientMoneyHttp.CreateAsync(token, tenant.PublicId, ClientMoneyHttp.Body());
        var stale = await ClientMoneyHttp.RowVersionAsync(token, tenant.PublicId, id);
        (await ClientMoneyHttp.SetActiveAsync(token, tenant.PublicId, id, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var route = $"{ClientMoneyHttp.Path(tenant.PublicId)}/{id}/{action}";

        var missing = await TenantHttp.Send(HttpMethod.Post, route, token, new { });
        (await CustodialHttp.ErrorsAsync(missing)).ShouldContainKey("RowVersion");

        var conflict = await TenantHttp.Send(HttpMethod.Post, route, token, new { rowVersion = stale });
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(conflict)).Code.ShouldBe("stale_row_version");
    }

    [Test]
    public async Task DisabledTenant_Create_Returns400DisabledTarget()
    {
        var tenant = await TestApp.CreateTenantAsync("Money Disabled", "money-disabled");
        await DisableTenantAsync(tenant.Id);
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(
            HttpMethod.Post, ClientMoneyHttp.Path(tenant.PublicId), system.AccessToken, ClientMoneyHttp.Body());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(tenant.PublicId);
        (await ClientMoneyHttp.CountAsync(tenant.Id)).ShouldBe(0);
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsRowUnderThisTenantsPath_Returns404()
    {
        var north = await TestApp.CreateTenantAsync("North Money Scope", "north-money-scope");
        var south = await TestApp.CreateTenantAsync("South Money Scope", "south-money-scope");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var southRow = await ClientMoneyHttp.CreateAsync(token, south.PublicId, ClientMoneyHttp.Body());
        var rowVersion = await ClientMoneyHttp.RowVersionAsync(token, south.PublicId, southRow);
        var wrongPath = $"{ClientMoneyHttp.Path(north.PublicId)}/{southRow}";

        (await TenantHttp.Send(HttpMethod.Get, wrongPath, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Post, $"{wrongPath}/disable", token, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Post, $"{wrongPath}/enable", token, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListIdsAsync(token, ClientMoneyHttp.Path(north.PublicId))).ShouldBeEmpty();
        (await ClientMoneyHttp.GetAsync(token, south.PublicId, southRow)).GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    private const string Long101 =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private const string Long141 =
        "NNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNN";

    private static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(string path, Guid id)
    {
        yield return (HttpMethod.Get, path, null);
        yield return (HttpMethod.Get, $"{path}/{id}", null);
        yield return (HttpMethod.Post, path, ClientMoneyHttp.Body());
        yield return (HttpMethod.Post, $"{path}/{id}/disable", new { rowVersion = "AAAAAAAAAAE=" });
        yield return (HttpMethod.Post, $"{path}/{id}/enable", new { rowVersion = "AAAAAAAAAAE=" });
    }

    private static async Task<List<Guid>> ListIdsAsync(string token, string route)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, route, token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
