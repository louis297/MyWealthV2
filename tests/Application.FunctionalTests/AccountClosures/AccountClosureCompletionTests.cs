using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>Account closure R16–R19, R21: the payout completes the closure, writes off the remainder and closes the
/// Account in its own database transaction.</summary>
[Parallelizable]
public class AccountClosureCompletionTests : TestBase
{
    [Test]
    public async Task ThePayout_WritesOffTheRemainder_ClosesTheAccount_AndCompletesTheClosure()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 300.00m);
        await AccountClosureHttp.InsertCashAsync(world.AccountId, 0.004m);
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var (withdrawalId, payoutLine) = await AccountClosureHttp.PayOutAsync(world, id, 300.00m);

        (await BankFeedHttp.LineAsync(payoutLine)).Status.ShouldBe(BankStatementLineStatus.Matched);
        var withdrawal = await WithdrawalHttp.GetAsync(world.TenantAdmin, withdrawalId);
        withdrawal.GetProperty("status").GetString().ShouldBe("Paid");
        var item = await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id);
        item.GetProperty("status").GetString().ShouldBe("Completed");
        item.GetProperty("writeOffAmount").GetDecimal().ShouldBe(0.004m);
        item.GetProperty("completedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("endedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        var writeOffId = item.GetProperty("writeOffTransactionId").GetGuid();
        var writeOff = await AccountClosureHttp.TransactionAsync(writeOffId);
        writeOff.Type.ShouldBe(TransactionType.CloseOut);
        writeOff.SourceKind.ShouldBe(TransactionSourceKind.AccountClosure);
        writeOff.SourcePublicId.ShouldBe(id);
        writeOff.Memo.ShouldBe("Account closure remainder");
        writeOff.CreatedBy.ShouldBe(SystemUser.Name);
        writeOff.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-0.004m);
        var account = await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId);
        account.GetProperty("status").GetString().ShouldBe("Closed");
        account.GetProperty("cashBalance").GetDecimal().ShouldBe(0m);
        account.GetProperty("reservedCash").GetDecimal().ShouldBe(0m);
        account.GetProperty("closure").GetProperty("status").GetString().ShouldBe("Completed");
        account.GetProperty("closure").GetProperty("completedAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Test]
    public async Task ThePayout_OfAWholeBalance_ClosesTheAccount_WithNoWriteOff()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 300.00m);
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var before = await CustodialHttp.TransactionCountAsync(world.AccountId);

        await AccountClosureHttp.PayOutAsync(world, id, 300.00m);

        var item = await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id);
        item.GetProperty("status").GetString().ShouldBe("Completed");
        item.GetProperty("writeOffAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("writeOffTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(before + 1);
        (await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId)).GetProperty("status").GetString().ShouldBe("Closed");
    }

    [Test]
    public async Task APayout_ThatLeavesMoreThanARemainder_IsPosted_AndTheClosureEndsNotClosable_WithTheAccountOpen()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 300.00m);
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var withdrawalId = await AccountClosureHttp.ApproveAndSendAsync(world, id);
        // Against R16: cash arrives on the closing account without a flow.
        await AccountClosureHttp.InsertCashAsync(world.AccountId, 5.00m);

        world.AddPayoutLine(withdrawalId, 300.00m);
        await BankFeedHttp.RunAsync();

        (await WithdrawalHttp.GetAsync(world.TenantAdmin, withdrawalId)).GetProperty("status").GetString().ShouldBe("Paid");
        var item = await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("cancelReason").GetString().ShouldBe("NotClosable");
        item.GetProperty("endedAt").ValueKind.ShouldBe(JsonValueKind.String);
        item.GetProperty("writeOffAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        var account = await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId);
        account.GetProperty("status").GetString().ShouldBe("Open");
        account.GetProperty("cashBalance").GetDecimal().ShouldBe(5.00m);
        (await AccountClosureHttp.TransactionsOfKindAsync(world.AccountId, TransactionSourceKind.AccountClosure)).ShouldBe(0);
    }

    [Test]
    public async Task AReturnedPayout_AfterTheClosureCompleted_IsUnmatchedAccountClosed_AndTheClosureStaysCompleted()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 300.00m);
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var (withdrawalId, payoutLine) = await AccountClosureHttp.PayOutAsync(world, id, 300.00m);

        var returnLine = world.AddReturnLine(payoutLine, 300.00m);
        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(returnLine);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.AccountClosed);
        (await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id)).GetProperty("status").GetString().ShouldBe("Completed");
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, withdrawalId)).GetProperty("status").GetString().ShouldBe("Paid");
    }

    [Test]
    public async Task TheWriteOffWithThePayout_CannotBeReversedByHand_AfterAReopen()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 300.00m);
        await AccountClosureHttp.InsertCashAsync(world.AccountId, 0.004m);
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        await AccountClosureHttp.PayOutAsync(world, id, 300.00m);
        var writeOff = (await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id)).GetProperty("writeOffTransactionId").GetGuid();
        await AccountClosureHttp.ReopenAsync(world.TenantAdmin, world.AccountId);

        var errors = await CustodialHttp.ErrorsAsync(await CustodialHttp.ReverseAsync(world.TenantAdmin, writeOff));

        errors[string.Empty].ShouldBe(["This transaction was written by the platform and cannot be reversed by hand."]);
    }
}
