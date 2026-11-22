using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestDb;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestHttp;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R41: the bank reconciliation lists the bank's interest, the interest credited and the tax
/// withheld as three known differences.</summary>
[Parallelizable]
public class ReconciliationInterestTests : TestBase
{
    [Test]
    public async Task AfterAnInterestPostingAndABankInterestLine_TheRunIsAgreed_WithTheThreeKinds_AndNoOtherLedgerCash()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth);
        await world.ReceiveAsync(5000m, world.DepositReference);
        var end = MonthEnd(NextMonth);
        await AccrueAndPostAsync(world.AccountId, end, end);
        var posting = (await PostingsAsync(world.AccountId)).Single();
        var gross = posting.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount;
        var tax = posting.CashLegs.Single(leg => leg.Role == CashLegRole.Tax).Amount;
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            "interest-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Credit, 520m, "USD",
            "Bank interest", null, "Interest", Kind: StatementLineKind.Interest));
        await BankFeedHttp.RunAsync();

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([
            ("BankInterest", "Known", 520m, 1),
            ("InterestCredited", "Known", -gross, 1),
            ("TaxWithheld", "Known", -tax, 1)
        ]);
        Decimal(run, "difference").ShouldBe(520m - gross - tax);
        (await CustodialHttp.CashBalanceAsync(world.TenantAdmin.AccessToken, world.AccountId)).ShouldBe(5000m + gross + tax);
    }
}
