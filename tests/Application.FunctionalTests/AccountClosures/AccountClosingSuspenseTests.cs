using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>Account closure R14: receipts and returned payouts for a closing account wait in suspense.</summary>
[Parallelizable]
public class AccountClosingSuspenseTests : TestBase
{
    [Test]
    public async Task AReceipt_QuotingAClosingAccountsReference_IsUnmatchedAccountClosing_AndPostsNothing()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var before = await CustodialHttp.TransactionCountAsync(world.AccountId);

        var receipt = BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 25m, "USD", world.DepositReference);
        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(receipt.Id!);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.AccountClosing);
        line.MatchedAccountId.ShouldBeNull();
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(before);
        (await world.CashAsync()).ShouldBe((1000m, 1000m, 0m));
    }

    [Test]
    public async Task AReceipt_ForAnotherOpenCustodialAccount_IsStillMatched()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var spare = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Spare");
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);

        (await CustodialHttp.FundAsync(world.TenantAdmin, spare, 25m)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
    }

    [Test]
    public async Task AReturnedPayout_OfAnEarlierWithdrawalOfAClosingAccount_IsUnmatchedAccountClosing_AndPostsNothing()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (earlier, payoutLine) = await world.PaidAsync(200m);
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var before = await CustodialHttp.TransactionCountAsync(world.AccountId);

        var returnLine = world.AddReturnLine(payoutLine, 200m);
        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(returnLine);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.AccountClosing);
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(before);
        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).Single(row => row.PublicId == earlier)
            .Status.ShouldBe(WithdrawalStatus.Paid);
        (await world.CashAsync()).ShouldBe((800m, 800m, 0m));
    }
}
