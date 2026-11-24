using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>Safekeeping account routes and the connection list, in-kind transfers R2–R7.</summary>
[Parallelizable]
public class SafekeepingAccountsTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect_OnEveryRoute()
    {
        foreach (var (method, route, body) in Routes(SafekeepingHttp.Path(Guid.NewGuid()), Guid.NewGuid())
                     .Append((HttpMethod.Get, "/custodian-connections", null)))
        {
            var response = await TenantHttp.Send(method, route, accessToken: null, body);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task TenantAdminAdviserAndCustomer_Return403_OnEveryRoute()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Safekeeping Roles", "firm-sk-roles");
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@firm-sk-roles", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", "tess@firm-sk-roles", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(UserRole.Customer, "Cee", "cee@firm-sk-roles", "Password1!", tenant.Id, adviser.Id);
        var system = await TenantHttp.SignInSystemAdmin();
        var id = await SafekeepingHttp.CreateAsync(system.AccessToken, tenant.PublicId, SafekeepingHttp.Body());

        var tokens = new[]
        {
            (await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-sk-roles", "Password1!", "firm-sk-roles")).AccessToken,
            (await FunctionalTestSetup.Oidc.SignInAsync("ann@firm-sk-roles", "Password1!", "firm-sk-roles")).AccessToken,
            (await TestApp.IssueAccessTokenAsync(customer)).AccessToken
        };

        foreach (var token in tokens)
        {
            foreach (var (method, route, body) in Routes(SafekeepingHttp.Path(tenant.PublicId), id)
                         .Append((HttpMethod.Get, "/custodian-connections", null)))
            {
                (await TenantHttp.Send(method, route, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
            }
        }

        (await SafekeepingHttp.CountAsync(tenant.Id)).ShouldBe(1);
    }

    [Test]
    public async Task UnknownTenant_Returns404()
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var path = SafekeepingHttp.Path(Guid.NewGuid());

        (await TenantHttp.Send(HttpMethod.Get, path, system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Post, path, system.AccessToken, SafekeepingHttp.Body()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Get, $"{path}/{Guid.NewGuid()}", system.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Create_Returns201WithLocation_AndGetReturnsTheFields()
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Create", "sk-create");
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Post, SafekeepingHttp.Path(tenant.PublicId), system.AccessToken, new
        {
            currency = "usd",
            custodianName = "  Simulator Custody ",
            accountName = " North Advisory Nominees Ltd ",
            accountNumber = " SIM-DEMO-CUSTODY-USD-1 ",
            custodianConnection = "simulator"
        });

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        Guid id;
        using (var json = JsonDocument.Parse(content))
        {
            id = json.RootElement.GetProperty("id").GetGuid();
        }

        response.Headers.Location!.ToString().ShouldBe($"/tenants/{tenant.PublicId}/safekeeping-accounts/{id}");
        var item = await SafekeepingHttp.GetAsync(system.AccessToken, tenant.PublicId, id);
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("custodianName").GetString().ShouldBe("Simulator Custody");
        item.GetProperty("accountName").GetString().ShouldBe("North Advisory Nominees Ltd");
        item.GetProperty("accountNumber").GetString().ShouldBe("SIM-DEMO-CUSTODY-USD-1");
        item.GetProperty("custodianConnection").GetString().ShouldBe("simulator");
        item.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        item.GetProperty("custodianFeed").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task Get_ShowsTheFeedStatus_OnceAReadWasTried()
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Feed", "sk-feed");
        var system = await TenantHttp.SignInSystemAdmin();
        var id = await SafekeepingHttp.CreateAsync(system.AccessToken, tenant.PublicId, SafekeepingHttp.Body());
        var at = new DateTimeOffset(2026, 11, 24, 1, 0, 0, TimeSpan.Zero);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.SafekeepingAccounts.IgnoreQueryFilters().SingleAsync(r => r.PublicId == id);
            var cursor = CustodianFeedCursor.Start(tenant.Id, row.Id);
            cursor.StorePage("w:1", at);
            cursor.RecordFailure(CustodianFailure.UnknownAccount, at.AddMinutes(1));
            db.CustodianFeedCursors.Add(cursor);
            await db.SaveChangesAsync();
        }

        var feed = (await SafekeepingHttp.GetAsync(system.AccessToken, tenant.PublicId, id)).GetProperty("custodianFeed");

        feed.GetProperty("lastSucceededAt").GetDateTimeOffset().ShouldBe(at);
        feed.GetProperty("lastFailedAt").GetDateTimeOffset().ShouldBe(at.AddMinutes(1));
        feed.GetProperty("lastFailure").GetString().ShouldBe("UnknownAccount");
        feed.GetProperty("positionsAt").GetDateTimeOffset().ShouldBe(at);
    }

    [Test]
    public async Task List_SortsByCurrencyThenActiveThenNewest()
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping List", "sk-list");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var usdOld = await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body("USD"));
        (await SafekeepingHttp.SetActiveAsync(token, tenant.PublicId, usdOld, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var usdActive = await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body("USD"));
        var nzd = await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body("NZD"));

        (await ListIdsAsync(token, SafekeepingHttp.Path(tenant.PublicId))).ShouldBe([nzd, usdActive, usdOld]);
    }

    [TestCase("currency", null)]
    [TestCase("currency", "XXX")]
    [TestCase("custodianName", "")]
    [TestCase("custodianName", Long101)]
    [TestCase("accountName", " ")]
    [TestCase("accountName", Long141)]
    [TestCase("accountNumber", "")]
    [TestCase("accountNumber", "02/0100")]
    public async Task Create_FieldOutsideItsRule_Returns400AtThatField(string field, string? value)
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Fields", "sk-fields");
        var system = await TenantHttp.SignInSystemAdmin();
        var body = new Dictionary<string, object?>
        {
            ["currency"] = "USD",
            ["custodianName"] = "Simulator Custody",
            ["accountName"] = "Nominees",
            ["accountNumber"] = SafekeepingHttp.UniqueNumber(),
            ["custodianConnection"] = SafekeepingHttp.Connection,
            [field] = value
        };

        var response = await TenantHttp.Send(HttpMethod.Post, SafekeepingHttp.Path(tenant.PublicId), system.AccessToken, body);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(field);
        (await SafekeepingHttp.CountAsync(tenant.Id)).ShouldBe(0);
    }

    [TestCase(null)]
    [TestCase("other-custodian")]
    [TestCase("Simulator")]
    public async Task Create_WithoutAConfiguredConnection_Returns400AtCustodianConnection(string? connection)
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Connection", "sk-connection");
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Post, SafekeepingHttp.Path(tenant.PublicId), system.AccessToken,
            SafekeepingHttp.Body(custodianConnection: connection));

        (await CustodialHttp.ErrorsAsync(response))["CustodianConnection"].ShouldBe(["Unknown custodian connection."]);
    }

    [TestCase("isActive", false)]
    [TestCase("tenantId", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    [TestCase("id", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    public async Task Create_BodyCarriesAServerField_Returns400(string field, object value)
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Server Field", "sk-server-field");
        var system = await TenantHttp.SignInSystemAdmin();
        var body = new Dictionary<string, object?>
        {
            ["currency"] = "USD",
            ["custodianName"] = "Simulator Custody",
            ["accountName"] = "Nominees",
            ["accountNumber"] = SafekeepingHttp.UniqueNumber(),
            ["custodianConnection"] = SafekeepingHttp.Connection,
            [field] = value
        };

        var response = await TenantHttp.Send(HttpMethod.Post, SafekeepingHttp.Path(tenant.PublicId), system.AccessToken, body);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(field);
    }

    [Test]
    public async Task SecondActiveInTheCurrency_Returns400_AnotherCurrencyCreates_AndEnablingTheFirstReturns400()
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Per Currency", "sk-per-currency");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var first = await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body("USD"));

        var second = await TenantHttp.Send(HttpMethod.Post, SafekeepingHttp.Path(tenant.PublicId), token, SafekeepingHttp.Body("USD"));
        (await CustodialHttp.ErrorsAsync(second))["Currency"].ShouldBe(["The tenant already has an active safekeeping account in USD."]);
        await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body("NZD"));

        (await SafekeepingHttp.SetActiveAsync(token, tenant.PublicId, first, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body("USD"));
        var enable = await SafekeepingHttp.SetActiveAsync(token, tenant.PublicId, first, active: true);
        (await CustodialHttp.ErrorsAsync(enable))["Currency"].ShouldBe(["The tenant already has an active safekeeping account in USD."]);
    }

    [Test]
    public async Task AccountNumberActiveInAnotherTenant_Returns400()
    {
        var north = await TestApp.CreateTenantAsync("North Safekeeping Number", "north-sk-number");
        var south = await TestApp.CreateTenantAsync("South Safekeeping Number", "south-sk-number");
        var system = await TenantHttp.SignInSystemAdmin();
        var number = SafekeepingHttp.UniqueNumber();
        await SafekeepingHttp.CreateAsync(system.AccessToken, north.PublicId, SafekeepingHttp.Body("USD", number));

        var clash = await TenantHttp.Send(HttpMethod.Post, SafekeepingHttp.Path(south.PublicId), system.AccessToken,
            SafekeepingHttp.Body("NZD", number.ToLowerInvariant()));

        (await CustodialHttp.ErrorsAsync(clash))["AccountNumber"]
            .ShouldBe(["This account number is already in use by an active safekeeping account."]);
    }

    [Test]
    public async Task DisableAndEnable_AreIdempotent()
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Toggle", "sk-toggle");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var id = await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body());

        (await SafekeepingHttp.SetActiveAsync(token, tenant.PublicId, id, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterDisable = await SafekeepingHttp.RowVersionAsync(token, tenant.PublicId, id);
        (await SafekeepingHttp.SetActiveAsync(token, tenant.PublicId, id, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SafekeepingHttp.RowVersionAsync(token, tenant.PublicId, id)).ShouldBe(afterDisable);
        (await SafekeepingHttp.GetAsync(token, tenant.PublicId, id)).GetProperty("isActive").GetBoolean().ShouldBeFalse();

        (await SafekeepingHttp.SetActiveAsync(token, tenant.PublicId, id, active: true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SafekeepingHttp.GetAsync(token, tenant.PublicId, id)).GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [TestCase("disable")]
    [TestCase("enable")]
    public async Task MissingRowVersion_Returns400_AndStaleRowVersion_Returns409(string action)
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Version", "sk-version");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var id = await SafekeepingHttp.CreateAsync(token, tenant.PublicId, SafekeepingHttp.Body());
        var stale = await SafekeepingHttp.RowVersionAsync(token, tenant.PublicId, id);
        (await SafekeepingHttp.SetActiveAsync(token, tenant.PublicId, id, active: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var route = $"{SafekeepingHttp.Path(tenant.PublicId)}/{id}/{action}";

        (await CustodialHttp.ErrorsAsync(await TenantHttp.Send(HttpMethod.Post, route, token, new { }))).ShouldContainKey("RowVersion");

        var conflict = await TenantHttp.Send(HttpMethod.Post, route, token, new { rowVersion = stale });
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(conflict)).Code.ShouldBe("stale_row_version");
    }

    [Test]
    public async Task NoPut()
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Put", "sk-put");
        var system = await TenantHttp.SignInSystemAdmin();
        var id = await SafekeepingHttp.CreateAsync(system.AccessToken, tenant.PublicId, SafekeepingHttp.Body());

        var response = await TenantHttp.Send(HttpMethod.Put, $"{SafekeepingHttp.Path(tenant.PublicId)}/{id}", system.AccessToken,
            SafekeepingHttp.Body());

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisabledTenant_Create_Returns400DisabledTarget()
    {
        var tenant = await TestApp.CreateTenantAsync("Safekeeping Disabled", "sk-disabled");
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Tenants.SingleAsync(row => row.Id == tenant.Id)).Disable();
            await db.SaveChangesAsync();
        }

        var system = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(HttpMethod.Post, SafekeepingHttp.Path(tenant.PublicId), system.AccessToken, SafekeepingHttp.Body());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
        (await SafekeepingHttp.CountAsync(tenant.Id)).ShouldBe(0);
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsRowUnderThisTenantsPath_Returns404()
    {
        var north = await TestApp.CreateTenantAsync("North Safekeeping Scope", "north-sk-scope");
        var south = await TestApp.CreateTenantAsync("South Safekeeping Scope", "south-sk-scope");
        var system = await TenantHttp.SignInSystemAdmin();
        var token = system.AccessToken;
        var southRow = await SafekeepingHttp.CreateAsync(token, south.PublicId, SafekeepingHttp.Body());
        var rowVersion = await SafekeepingHttp.RowVersionAsync(token, south.PublicId, southRow);
        var wrongPath = $"{SafekeepingHttp.Path(north.PublicId)}/{southRow}";

        (await TenantHttp.Send(HttpMethod.Get, wrongPath, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Post, $"{wrongPath}/disable", token, new { rowVersion })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(HttpMethod.Post, $"{wrongPath}/enable", token, new { rowVersion })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListIdsAsync(token, SafekeepingHttp.Path(north.PublicId))).ShouldBeEmpty();
    }

    [Test]
    public async Task CustodianConnections_SystemAdmin_GetsNameAndAdapterOnly()
    {
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Get, "/custodian-connections", system.AccessToken);

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("name").GetString().ShouldBe("simulator");
        item.GetProperty("adapter").GetString().ShouldBe("Simulator");
        item.EnumerateObject().Select(property => property.Name).ShouldBe(["name", "adapter"], ignoreOrder: true);
    }

    private const string Long101 =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private const string Long141 =
        "NNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNN";

    private static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(string path, Guid id)
    {
        yield return (HttpMethod.Get, path, null);
        yield return (HttpMethod.Get, $"{path}/{id}", null);
        yield return (HttpMethod.Post, path, SafekeepingHttp.Body());
        yield return (HttpMethod.Post, $"{path}/{id}/disable", new { rowVersion = "AAAAAAAAAAE=" });
        yield return (HttpMethod.Post, $"{path}/{id}/enable", new { rowVersion = "AAAAAAAAAAE=" });
    }

    private static async Task<List<Guid>> ListIdsAsync(string token, string route)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, route, token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }
}
