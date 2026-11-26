using System.Net;
using MyWealthV2.Application.FunctionalTests.CorporateActions;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>
/// Account valuation R18 on a Custodial account: the entitlement posting of a hand-entered corporate action is booked
/// after its ex-date, so the ex-date is valued with the quantity restated; after "Apply now" with no price on the new
/// basis the line is not valued. The holdings are moved back in the database before the ex-date, as the interest
/// tests do, because a Custodial account cannot be dated by a caller.
/// </summary>
[Parallelizable]
public class CustodialBasisTests : TestBase
{
    [Test]
    public async Task TheExDate_IsValuedWithTheQuantityRestatedToThePriceOnTheNewBasis()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        var exDate = Today.AddDays(-3);
        await InterestDb.BackdateAsync(holders.Ned, exDate.AddDays(-2));
        await AddPriceAsync(holders.InstrumentId, exDate.AddDays(-1), 25.00m);
        await AddPriceAsync(holders.InstrumentId, exDate, 12.50m);
        await CreateAsync(token, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: exDate));
        (await RunApplyAsync()).Posted.ShouldBe(2);

        var before = ValuationHttp.Holding(await ValuationHttp.ReadAsync(token, holders.Ned, exDate.AddDays(-1)), "AAPL");
        (before.GetProperty("quantity").GetDecimal(), before.GetProperty("ledgerQuantity").GetDecimal()).ShouldBe((100m, 100m));
        before.GetProperty("value").GetDecimal().ShouldBe(2_500.00m);

        var exDay = ValuationHttp.Holding(await ValuationHttp.ReadAsync(token, holders.Ned, exDate), "AAPL");
        exDay.GetProperty("status").GetString().ShouldBe("Valued");
        (exDay.GetProperty("quantity").GetDecimal(), exDay.GetProperty("ledgerQuantity").GetDecimal()).ShouldBe((200m, 100m));
        exDay.GetProperty("price").GetProperty("amount").GetDecimal().ShouldBe(12.50m);
        exDay.GetProperty("value").GetDecimal().ShouldBe(2_500.00m);

        var today = ValuationHttp.Holding(await ValuationHttp.ReadAsync(token, holders.Ned, Today), "AAPL");
        (today.GetProperty("quantity").GetDecimal(), today.GetProperty("ledgerQuantity").GetDecimal()).ShouldBe((200m, 200m));
    }

    [Test]
    public async Task AfterApplyNow_WithNoPriceOnTheNewBasis_TheLineIsPriceBeforeCorporateAction()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        await AddPriceAsync(holders.InstrumentId, Today.AddDays(-1), 25.00m);
        var id = await CreateAsync(token, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        var rowVersion = await RowVersionAsync(token, id);
        (await Send(HttpMethod.Post, $"{Root}/{id}/apply", token, new { rowVersion })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RunApplyAsync()).Posted.ShouldBe(2);

        var valuation = await ValuationHttp.ReadAsync(token, holders.Ned, Today);

        var line = ValuationHttp.Holding(valuation, "AAPL");
        line.GetProperty("status").GetString().ShouldBe("PriceBeforeCorporateAction");
        (line.GetProperty("quantity").GetDecimal(), line.GetProperty("ledgerQuantity").GetDecimal()).ShouldBe((200m, 200m));
        line.GetProperty("price").GetProperty("date").GetString().ShouldBe(Today.AddDays(-1).ToString("yyyy-MM-dd"));
        line.GetProperty("value").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        valuation.GetProperty("complete").GetBoolean().ShouldBeFalse();
    }
}
