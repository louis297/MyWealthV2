using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R44, R45: a dividend in the bank reconciliation of its client money account.</summary>
[Parallelizable]
public class DividendReconciliationTests : TestBase
{
    private const decimal Expected = 93.50m;

    private static decimal Amount(JsonElement run, string kind) =>
        Items(run).Where(item => item.Kind == kind).Sum(item => item.Amount);

    [Test]
    public async Task AReceivedDividendNotPostedYet_IsDividendsNotPosted()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected);

        var run = await RunAndGetAsync(dividend.TenantAdmin, dividend.ClientMoney.PublicId);

        Items(run).ShouldContain(("DividendsNotPosted", "Timing", Expected, 1));
        Items(run).ShouldNotContain(item => item.Kind == "DividendDifference");
    }

    [Test]
    public async Task APostedDividend_WhoseAmountsAgree_MakesNoDividendItem_ItsTaxIsTaxWithheld_AndTheRestIsUnchanged()
    {
        var dividend = await DividendWorld.CreateAsync();
        var before = await RunAndGetAsync(dividend.TenantAdmin, dividend.ClientMoney.PublicId);
        await dividend.ReceiveAsync(Expected);
        await RunApplyAsync();

        var run = await RunAndGetAsync(dividend.TenantAdmin, dividend.ClientMoney.PublicId);

        Items(run).ShouldNotContain(item => item.Kind == "DividendsNotPosted" || item.Kind == "DividendDifference");
        // RWT of 18.00 and 1.80, held in the trust account until it is paid over.
        Items(run).ShouldContain(("TaxWithheld", "Known", 19.80m, 2));
        Amount(run, "OtherLedgerCash").ShouldBe(Amount(before, "OtherLedgerCash"));
        Amount(run, "Unexplained").ShouldBe(Amount(before, "Unexplained"));
        Items(run).Sum(item => item.Amount).ShouldBe(Decimal(run, "difference")!.Value);
    }

    [Test]
    public async Task AReceiptWithinTheAllowance_LeavesADividendDifference_OnceItIsPosted()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected + 0.03m);
        await RunApplyAsync();

        var run = await RunAndGetAsync(dividend.TenantAdmin, dividend.ClientMoney.PublicId);

        Items(run).ShouldContain(("DividendDifference", "Known", 0.03m, 1));
    }

    [Test]
    public async Task TheShareOfAnAccountInAnotherCurrency_IsInTheDividendDifference_OfTheQuoteCurrency()
    {
        var dividend = await DividendWorld.CreateAsync(setUp: async (world, holders) =>
        {
            var nzd = await Interest.InterestHttp.OpenCustodialAsync(world.Tenant.Id, world.TenantAdmin.AccessToken, world.Customer.PublicId, "NZD");
            await CustodialHttp.FundAsync(world.TenantAdmin.AccessToken, nzd, 2000m);
            await CustodialHttp.InsertManualTradeAsync(nzd, "Buy", holders.InstrumentId, 20m);
        });
        await Rebalances.RebalanceHttp.SetRateAsync("USD", "NZD", 1.7m);
        await dividend.ReceiveAsync(Expected + 17.00m);
        await RunApplyAsync();

        var run = await RunAndGetAsync(dividend.TenantAdmin, dividend.ClientMoney.PublicId);

        Items(run).ShouldContain(("DividendDifference", "Known", 17.00m, 1));
    }
}
