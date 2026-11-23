using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Tenants;
using static MyWealthV2.Application.FunctionalTests.MigrationBatches.MigrationBatchHttp;

namespace MyWealthV2.Application.FunctionalTests.MigrationBatches;

/// <summary>Migration batches R1–R11, R28, R29: access, create, replace, the reads and the issues on the item.</summary>
[Parallelizable]
public class MigrationBatchSaveTests : TestBase
{
    [Test]
    public async Task WithoutBearer_EveryRoute_Returns401WithoutRedirect()
    {
        foreach (var (method, path, body) in Routes(System.Guid.NewGuid()))
        {
            var response = await TenantHttp.Send(method, path, null, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task AdviserCustomerAndSystemAdmin_Return403_OnEveryRoute()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var system = await TenantHttp.SignInSystemAdmin();

        foreach (var token in new[] { world.AdviserToken.AccessToken, world.CustomerToken.AccessToken, system.AccessToken })
        {
            foreach (var (method, path, body) in Routes(id))
            {
                (await TenantHttp.Send(method, path, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            }
        }
    }

    [Test]
    public async Task AnotherTenantsBatch_Returns404_OnEveryRouteWithAnId_AndIsNotListed()
    {
        var north = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(north.TenantAdmin, north.ValidBody());
        var south = await MigrationWorld.CreateAsync("South Advisory", "south-advisory");

        foreach (var (method, path, body) in Routes(id).Where(route => route.Path != MigrationBatchHttp.Path))
        {
            (await TenantHttp.Send(method, path, south.TenantAdmin, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        Ids(await ListAsync(south.TenantAdmin)).ShouldBeEmpty();
        Ids(await ListAsync(north.TenantAdmin)).ShouldBe([id]);
        (await TenantHttp.Send(HttpMethod.Get, $"{MigrationBatchHttp.Path}/{System.Guid.NewGuid()}", north.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Create_Returns201_AndTheItemIsADraft_WithTheBody_TheTotals_AndNoIssueButTheFunding()
    {
        var world = await MigrationWorld.CreateAsync();
        var reference = await BankFeedHttp.DepositReferenceAsync(world.AccountId);

        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());

        var item = await GetAsync(world.TenantAdmin, id);
        item.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "name", "previousProvider", "asOfDate", "status", "entryCount", "holdingCount", "createdBy", "createdAt",
            "submittedBy", "submittedAt", "postedBy", "postedAt", "controlTotals", "computedTotals", "fundingTotals",
            "funding", "entries", "validation", "returned", "cancelled", "rowVersion"
        ], ignoreOrder: true);
        (item.GetProperty("id").GetGuid(), item.GetProperty("name").GetString(), item.GetProperty("previousProvider").GetString(),
            item.GetProperty("asOfDate").GetString(), item.GetProperty("status").GetString())
            .ShouldBe((id, "Acme Wealth transfer, November 2026", "Acme Wealth", "2026-11-20", "Draft"));
        (item.GetProperty("entryCount").GetInt32(), item.GetProperty("holdingCount").GetInt32()).ShouldBe((2, 2));
        item.GetProperty("createdBy").GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        item.GetProperty("submittedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        var computed = item.GetProperty("computedTotals");
        computed.GetProperty("accountCount").GetInt32().ShouldBe(2);
        Amounts(computed.GetProperty("cash")).ShouldBe([("USD", 250000m)]);
        Amounts(computed.GetProperty("cost")).ShouldBe([("USD", 264000m)]);
        var quantity = computed.GetProperty("quantity").EnumerateArray().Single();
        (quantity.GetProperty("instrumentId").GetGuid(), quantity.GetProperty("quantity").GetDecimal()).ShouldBe((world.InstrumentId, 1200m));
        Amounts(item.GetProperty("controlTotals").GetProperty("cash")).ShouldBe([("USD", 250000m)]);
        Amounts(item.GetProperty("fundingTotals")).ShouldBe([("USD", 0m)]);
        item.GetProperty("funding").GetArrayLength().ShouldBe(0);
        var validation = item.GetProperty("validation");
        validation.GetProperty("isValid").GetBoolean().ShouldBeFalse();
        var issue = validation.GetProperty("issues").EnumerateArray().Single();
        (issue.GetProperty("code").GetString(), issue.GetProperty("currency").GetString(), issue.GetProperty("message").GetString())
            .ShouldBe(("FundingMismatch", "USD", "Bank receipts for this batch add up to 0.00 USD; its cash is 250,000.00 USD."));
        var entry = item.GetProperty("entries")[0];
        var account = entry.GetProperty("account");
        (account.GetProperty("id").GetGuid(), account.GetProperty("depositReference").GetString(), account.GetProperty("currency").GetString())
            .ShouldBe((world.AccountId, reference, "USD"));
        account.GetProperty("customer").GetProperty("id").GetGuid().ShouldBe(world.Bank.Customer.PublicId);
        (entry.GetProperty("cash").GetDecimal(), entry.GetProperty("externalReference").GetString()).ShouldBe((150000m, "ACME-" + world.AccountId.ToString("N")[..6]));
        var holding = entry.GetProperty("holdings")[0];
        (holding.GetProperty("instrument").GetProperty("symbol").GetString(), holding.GetProperty("quantity").GetDecimal(),
            holding.GetProperty("cost").GetDecimal()).ShouldBe(("VTI", 800m, 176000m));
        entry.GetProperty("openingTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("entries")[1].GetProperty("account").GetProperty("id").GetGuid().ShouldBe(world.SecondAccountId);
        item.GetProperty("returned").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task Create_AnEmptyBatch_Returns201_WithTheIssueNoEntries()
    {
        var world = await MigrationWorld.CreateAsync();

        var id = await CreateIdAsync(world.TenantAdmin, Body([]));

        Codes(await GetAsync(world.TenantAdmin, id)).ShouldBe(["NoEntries"]);
    }

    [Test]
    public async Task Replace_Returns204_AndTheItemShowsTheNewBody()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());

        var response = await ReplaceAsync(world.TenantAdmin, id, Body(
            [Entry(world.SecondAccountId, 5m)],
            Totals(1, [Money("USD", 5m)]),
            name: "Second try", previousProvider: "Beta", asOfDate: "2026-11-21"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await GetAsync(world.TenantAdmin, id);
        (item.GetProperty("name").GetString(), item.GetProperty("previousProvider").GetString(), item.GetProperty("asOfDate").GetString())
            .ShouldBe(("Second try", "Beta", "2026-11-21"));
        (item.GetProperty("entryCount").GetInt32(), item.GetProperty("holdingCount").GetInt32()).ShouldBe((1, 0));
        item.GetProperty("entries")[0].GetProperty("account").GetProperty("id").GetGuid().ShouldBe(world.SecondAccountId);
        Codes(item).ShouldBe(["FundingMismatch"]);
    }

    [Test]
    public async Task Replace_WithAStaleRowVersion_Returns409_AndWithoutOne_Returns400()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());

        var stale = await TenantHttp.Send(HttpMethod.Put, $"{MigrationBatchHttp.Path}/{id}", world.TenantAdmin, Body([], rowVersion: StaleRowVersion));
        var missing = await TenantHttp.Send(HttpMethod.Put, $"{MigrationBatchHttp.Path}/{id}", world.TenantAdmin, Body([]));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(stale)).Code.ShouldBe("stale_row_version");
        (await CustodialHttp.ErrorsAsync(missing)).ShouldContainKey("RowVersion");
    }

    [Test]
    public async Task AnUnknownOrAnotherTenantsId_Returns400_AtItsPlaceInTheBody()
    {
        var world = await MigrationWorld.CreateAsync();
        var south = await MigrationWorld.CreateAsync("South Advisory", "south-advisory");

        var response = await CreateAsync(world.TenantAdmin, Body(
            [
                Entry(world.AccountId, 1m),
                Entry(south.AccountId, 1m),
                Entry(System.Guid.NewGuid(), null, Holding(south.InstrumentId, 1m, 1m))
            ],
            Totals(3, quantity: [Quantity(System.Guid.NewGuid(), 1m)])));

        var errors = await CustodialHttp.ErrorsAsync(response);
        errors.Keys.ShouldBe(["Entries[1].AccountId", "Entries[2].AccountId", "Entries[2].Holdings[0].InstrumentId",
            "ControlTotals.Quantity[0].InstrumentId"], ignoreOrder: true);
        errors.Values.SelectMany(messages => messages).Distinct().ShouldBe(["Not found in this tenant."]);
        Ids(await ListAsync(world.TenantAdmin)).ShouldBeEmpty();
    }

    [Test]
    public async Task ABadShape_Returns400_AtTheField()
    {
        var world = await MigrationWorld.CreateAsync();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");

        var errors = await CustodialHttp.ErrorsAsync(await CreateAsync(world.TenantAdmin, Body(
            [Entry(world.AccountId, 0m), Entry(world.AccountId, null), Entry(world.SecondAccountId, 1m, Holding(world.InstrumentId, 0m, -1m))],
            Totals(-1, [Money("USD", -1m)]),
            name: " ", asOfDate: tomorrow)));

        errors.Keys.ShouldBe([
            "Name", "AsOfDate", "ControlTotals.AccountCount", "ControlTotals.Cash[0].Amount", "Entries[0].Cash", "Entries[1].AccountId",
            "Entries[1].Cash", "Entries[2].Holdings[0].Quantity", "Entries[2].Holdings[0].Cost"
        ], ignoreOrder: true);
    }

    [Test]
    public async Task ATenantId_InTheBodyOrTheQuery_Returns400()
    {
        var world = await MigrationWorld.CreateAsync();
        var body = world.ValidBody();
        body["tenantId"] = System.Guid.NewGuid();

        (await CreateAsync(world.TenantAdmin, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantHttp.Send(HttpMethod.Get, $"{MigrationBatchHttp.Path}?tenantId={System.Guid.NewGuid()}", world.TenantAdmin)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TheItem_HasOneIssuePerCause()
    {
        var world = await MigrationWorld.CreateAsync();
        var token = world.TenantAdmin;
        var customer = world.Bank.Customer.PublicId;
        var external = await CustodialHttp.OpenExternalAsync(token, customer);
        var closed = await CustodialHttp.OpenCustodialAsync(token, customer, "Closed portfolio");
        await CloseInDatabaseAsync(closed);
        (await CustodialHttp.FundAsync(token, world.SecondAccountId, 10m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var olga = await TestApp.CreatePersonAsync(MyWealthV2.Domain.Enums.UserRole.Customer, "Olga Other", "olga@north-advisory.example",
            "Password1!", world.Tenant.Id, world.Bank.Adviser.Id);
        var olgaAccount = await CustodialHttp.OpenCustodialAsync(token, olga.PublicId, "Olga's portfolio");
        await BankFeedHttp.SetCustomerDisabledAsync(olga.Id, true);
        var precise = await CustodialHttp.OpenCustodialAsync(token, customer, "Precise portfolio");
        var disabled = await CustodialHttp.CreateInstrumentAsync(token, "OLD", "USD");
        await DisableInstrumentAsync(token, disabled);
        var nzd = await CustodialHttp.CreateInstrumentAsync(token, "AIR", "NZD");

        var id = await CreateIdAsync(token, Body(
            [
                Entry(external, 1m),
                Entry(closed, 1m),
                Entry(olgaAccount, 1m),
                Entry(world.SecondAccountId, 1m),
                Entry(precise, 0.001m),
                Entry(world.AccountId, null, Holding(disabled, 1m, 1m), Holding(nzd, 1m, 1m))
            ],
            Totals(5, [Money("USD", 4m)], [Quantity(disabled, 2m)], [Money("USD", 1m), Money("NZD", 2m)])));

        var issues = (await GetAsync(token, id)).GetProperty("validation").GetProperty("issues").EnumerateArray()
            .Select(issue => (Code: issue.GetProperty("code").GetString(), Account: OptionalId(issue, "accountId"),
                Instrument: OptionalId(issue, "instrumentId"), Total: issue.GetProperty("total").GetString(),
                Currency: issue.GetProperty("currency").GetString()))
            .ToList();
        issues.ShouldBe([
            ("AccountNotCustodial", external, null, null, null),
            ("AccountNotOpen", closed, null, null, null),
            ("CustomerDisabled", olgaAccount, null, null, null),
            ("AccountNotEmpty", world.SecondAccountId, null, null, null),
            ("CashPrecision", precise, null, null, null),
            ("InstrumentInactive", world.AccountId, disabled, null, null),
            ("InstrumentCurrencyMismatch", world.AccountId, nzd, null, null),
            ("ControlTotalMismatch", null, null, "AccountCount", null),
            ("ControlTotalMismatch", null, null, "Cash", "USD"),
            ("ControlTotalMismatch", null, disabled, "Quantity", null),
            ("ControlTotalMismatch", null, nzd, "Quantity", null),
            ("ControlTotalMismatch", null, null, "Cost", "NZD"),
            ("FundingMismatch", null, null, null, "USD")
        ]);
    }

    [Test]
    public async Task TheList_IsNewestFirst_AndFiltersByStatus()
    {
        var world = await MigrationWorld.CreateAsync();
        var first = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var second = await CreateIdAsync(world.TenantAdmin, Body([]));

        var all = await ListAsync(world.TenantAdmin);
        var drafts = await ListAsync(world.TenantAdmin, "?status=Draft,Submitted");
        var posted = await ListAsync(world.TenantAdmin, "?status=Posted");

        Ids(all).ShouldBe([second, first]);
        all.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var listed = all.GetProperty("items")[1];
        listed.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "name", "previousProvider", "asOfDate", "status", "entryCount", "holdingCount", "createdBy", "createdAt",
            "submittedBy", "submittedAt", "postedBy", "postedAt"
        ], ignoreOrder: true);
        (listed.GetProperty("entryCount").GetInt32(), listed.GetProperty("holdingCount").GetInt32()).ShouldBe((2, 2));
        Ids(drafts).ShouldBe([second, first]);
        Ids(posted).ShouldBeEmpty();
        (await CustodialHttp.ErrorsAsync(await TenantHttp.Send(HttpMethod.Get, $"{MigrationBatchHttp.Path}?status=Done", world.TenantAdmin)))
            .ShouldContainKey("Status");
    }

    private static List<(string?, decimal)> Amounts(JsonElement rows) =>
        rows.EnumerateArray().Select(row => (row.GetProperty("currency").GetString(), row.GetProperty("amount").GetDecimal())).ToList();

    private static Guid? OptionalId(JsonElement issue, string name) =>
        issue.GetProperty(name).ValueKind == JsonValueKind.Null ? null : issue.GetProperty(name).GetGuid();

    private static async Task DisableInstrumentAsync(string token, Guid id)
    {
        var get = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{id}", token);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        (await InstrumentHttp.Send(HttpMethod.Post, $"/instruments/{id}/disable", token, new { rowVersion })).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }
}
