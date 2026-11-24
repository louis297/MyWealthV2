using System.Net;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Custody;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R46, R52: an account owed a dividend, on the closure and on the account item.</summary>
[Parallelizable]
public class DividendAccountTests : TestBase
{
    private const string PendingMessage = "A dividend is still to be paid on this account. Close it after the payment.";

    [Test]
    public async Task AnAccountThatSoldEverythingAfterTheExDate_CannotBeClosedUntilItsDividendIsPaid()
    {
        var dividend = await DividendWorld.CreateAsync();
        await CustodialHttp.InsertManualTradeAsync(dividend.Holders.Olga, "Sell", dividend.Holders.InstrumentId, 10m);

        var refused = await AccountClosureHttp.PostAsync(dividend.TenantAdmin, dividend.Holders.Olga, AccountClosureHttp.Body());

        (await CustodialHttp.ErrorsAsync(refused)).GetValueOrDefault("AccountId").ShouldBe([PendingMessage]);

        await dividend.ReceiveAsync(93.50m);
        await RunApplyAsync();
        var after = await AccountClosureHttp.PostAsync(dividend.TenantAdmin, dividend.Holders.Olga, AccountClosureHttp.Body());
        (await after.Content.ReadAsStringAsync()).ShouldNotContain(PendingMessage);
    }

    [Test]
    public async Task TheAccountItem_CountsThePendingDividends_AndShowsTheElection()
    {
        var dividend = await DividendWorld.CreateAsync();

        var ned = await AccountClosureHttp.AccountAsync(dividend.TenantAdmin, dividend.Holders.Ned);
        ned.GetProperty("reinvestDividends").GetBoolean().ShouldBeFalse();
        ned.GetProperty("pendingDividends").GetInt32().ShouldBe(1);
        var external = await AccountClosureHttp.AccountAsync(dividend.TenantAdmin, dividend.Holders.External);
        (external.GetProperty("reinvestDividends").GetBoolean(), external.GetProperty("pendingDividends").GetInt32()).ShouldBe((false, 0));
        var customer = await AccountClosureHttp.AccountAsync(dividend.World.CustomerToken.AccessToken, dividend.Holders.Ned);
        customer.GetProperty("pendingDividends").GetInt32().ShouldBe(1);
        var list = await CorporateActionHttp.GetAsync(dividend.TenantAdmin, $"/accounts?pageSize=100");
        list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == dividend.Holders.Ned)
            .GetProperty("pendingDividends").GetInt32().ShouldBe(1);

        await dividend.ReceiveAsync(93.50m);
        await RunApplyAsync();

        (await AccountClosureHttp.AccountAsync(dividend.TenantAdmin, dividend.Holders.Ned)).GetProperty("pendingDividends")
            .GetInt32().ShouldBe(0);
    }
}
