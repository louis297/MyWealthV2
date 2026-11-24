using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R6, R12–R16, R49, R51: a dividend goes ex.</summary>
[Parallelizable]
public class DividendGoingExTests : TestBase
{
    private static async Task<Guid> GoExNowAsync(NominatedWorld world, Dictionary<string, object?> body)
    {
        var id = await CreateAsync(world.TenantAdmin.AccessToken, body);
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);
        var response = await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return id;
    }

    [Test]
    public async Task ApplyNow_FixesAnEntitlementPerCustodialHolder_WithItsQuantityAndSourceAmounts_AndPostsNothing()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);

        var id = await GoExNowAsync(world, DividendBody(holders.InstrumentId, amount: 1.00m, exDate: Today));
        await RunApplyAsync();

        var entitlements = await EntitlementsAsync(id);
        entitlements.Select(row => row.AccountId).ShouldBe([await AccountIdAsync(holders.Ned), await AccountIdAsync(holders.Olga)]);
        entitlements.ShouldAllBe(row => row.Status == EntitlementStatus.Pending && row.TransactionId == null);
        entitlements.Select(row => (row.EntitledQuantity, row.GrossAmount, row.ForeignTax, row.ImputationCredit)).ShouldBe([
            ((decimal?)100m, (decimal?)100.00m, (decimal?)15.00m, (decimal?)0m),
            ((decimal?)10m, (decimal?)10.00m, (decimal?)1.50m, (decimal?)0m)
        ]);
        entitlements.ShouldAllBe(row => !row.Reinvests && row.ParticipatingQuantity == null);

        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("status").GetString().ShouldBe("Effective");
        var payment = item.GetProperty("payment");
        payment.GetProperty("expectedAmount").GetDecimal().ShouldBe(93.50m);
        payment.GetProperty("currency").GetString().ShouldBe("USD");
        payment.GetProperty("receivedAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        payment.GetProperty("difference").ValueKind.ShouldBe(JsonValueKind.Null);
        payment.GetProperty("statementLineId").ValueKind.ShouldBe(JsonValueKind.Null);
        payment.GetProperty("paidAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await TransactionsOfAsync(world.TenantAdmin.AccessToken, holders.Ned))
            .ShouldNotContain(row => row.GetProperty("type").GetString() == "Dividend");
    }

    [Test]
    public async Task ADividend_GoesExOnAPriceFromItsExDate()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var exDate = Today.AddDays(-1);
        var id = await CreateAsync(world.TenantAdmin.AccessToken, DividendBody(holders.InstrumentId, exDate: exDate));

        await RunApplyAsync();
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Announced");
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("payment").ValueKind.ShouldBe(JsonValueKind.Null);

        await AddPriceAsync(holders.InstrumentId, exDate, 25m);
        await RunApplyAsync();

        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Effective");
        (await EntitlementsAsync(id)).Count.ShouldBe(2);
    }

    [Test]
    public async Task ABuyAfterTheExDate_GetsNothing_AndASellAfterItKeepsTheEntitlement()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await GoExNowAsync(world, DividendBody(holders.InstrumentId, exDate: Today));

        await CustodialHttp.InsertManualTradeAsync(holders.Empty, "Buy", holders.InstrumentId, 2m);
        await CustodialHttp.InsertManualTradeAsync(holders.Olga, "Sell", holders.InstrumentId, 10m);
        await RunApplyAsync();

        var entitlements = await EntitlementsAsync(id);
        entitlements.Count.ShouldBe(2);
        entitlements.ShouldAllBe(row => row.Status == EntitlementStatus.Pending);
        entitlements.Single(row => row.AccountId == AccountIdOf(holders.Olga)).EntitledQuantity.ShouldBe(10m);
    }

    [Test]
    public async Task ADividendWithNoHolder_IsAppliedAtOnce_AndExpectsNoPayment()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "MSFT");

        var id = await GoExNowAsync(world, DividendBody(instrumentId, exDate: Today));

        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("status").GetString().ShouldBe("Applied");
        item.GetProperty("payment").GetProperty("expectedAmount").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task AnEntitlementThatRoundsToNothing_IsNotAppliedWithNothingToPay()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        await CustodialHttp.InsertManualTradeAsync(holders.Empty, "Buy", holders.InstrumentId, 0.001m);

        var id = await GoExNowAsync(world, DividendBody(holders.InstrumentId, amount: 0.72m, taxSource: "NewZealand", rate: 0m, exDate: Today));

        var tiny = (await EntitlementsAsync(id)).Single(row => row.AccountId == AccountIdOf(holders.Empty));
        (tiny.Status, tiny.Reason).ShouldBe((EntitlementStatus.NotApplied, (EntitlementReason?)EntitlementReason.NothingToPay));
        var page = await GetAsync(world.TenantAdmin.AccessToken, $"{Root}/{id}/entitlements?status=NotApplied");
        page.GetProperty("items")[0].GetProperty("reason").GetString().ShouldBe("NothingToPay");
    }

    [Test]
    public async Task TheEntitlementItem_ShowsTheDividendFields_AndASplitsShowsNone()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await GoExNowAsync(world, DividendBody(holders.InstrumentId, amount: 1.00m, exDate: Today));

        var item = (await GetAsync(world.TenantAdmin.AccessToken, $"{Root}/{id}/entitlements")).GetProperty("items")[0];

        item.GetProperty("accountId").GetGuid().ShouldBe(holders.Ned);
        item.GetProperty("status").GetString().ShouldBe("Pending");
        item.GetProperty("entitledQuantity").GetDecimal().ShouldBe(100m);
        item.GetProperty("grossAmount").GetDecimal().ShouldBe(100.00m);
        item.GetProperty("foreignTax").GetDecimal().ShouldBe(15.00m);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("reinvests").GetBoolean().ShouldBeFalse();
        item.GetProperty("participatingQuantity").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("reinvestedQuantity").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("netAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("netCurrency").ValueKind.ShouldBe(JsonValueKind.Null);

        var splitId = await GoExNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        var split = (await GetAsync(world.TenantAdmin.AccessToken, $"{Root}/{splitId}/entitlements")).GetProperty("items")[0];
        split.GetProperty("entitledQuantity").ValueKind.ShouldBe(JsonValueKind.Null);
        split.GetProperty("reinvests").ValueKind.ShouldBe(JsonValueKind.Null);
        split.GetProperty("currency").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ASplitsItem_HasNoPayment()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await GoExNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));

        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("payment").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task<List<JsonElement>> TransactionsOfAsync(string token, Guid accountId) =>
        (await GetAsync(token, $"/transactions?accountId={accountId}&pageSize=100")).GetProperty("items").EnumerateArray().ToList();

    private static int AccountIdOf(Guid accountId) => AccountIdAsync(accountId).GetAwaiter().GetResult();
}
