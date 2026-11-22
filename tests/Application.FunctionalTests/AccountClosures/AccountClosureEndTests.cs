using System.Net;
using System.Text.Json;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>Account closure R20: a closure ends with its withdrawal, in the same database transaction, and the Account
/// stays Open with its cash.</summary>
[Parallelizable]
public class AccountClosureEndTests : TestBase
{
    [Test]
    public async Task RejectingTheWithdrawal_CancelsTheClosure_AndTheAccountTakesWritesAgain()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var withdrawal = await AccountClosureHttp.WithdrawalIdAsync(world.TenantAdmin, world.AccountId, id);

        var response = await WithdrawalHttp.ActAsync(world.TenantAdmin, withdrawal, "reject", "Customer changed their mind");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        await ShouldBeCancelledAsync(world, id, "WithdrawalRejected");
        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
        (await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var receipt = BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 25m, "USD", world.DepositReference);
        await BankFeedHttp.RunAsync();
        (await BankFeedHttp.LineAsync(receipt.Id!)).Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task CancellingTheWithdrawal_BeforeApproval_CancelsTheClosure()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var withdrawal = await AccountClosureHttp.WithdrawalIdAsync(world.TenantAdmin, world.AccountId, id);

        var response = await WithdrawalHttp.ActAsync(world.Adviser, withdrawal, "cancel");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        await ShouldBeCancelledAsync(world, id, "WithdrawalCancelled");
    }

    [Test]
    public async Task CancellingTheWithdrawal_AfterApprovalBeforeTheFirstAttempt_CancelsTheClosure_AndANewClosureCanStart()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        var withdrawal = await AccountClosureHttp.WithdrawalIdAsync(world.TenantAdmin, world.AccountId, id);
        (await WithdrawalHttp.ActAsync(world.TenantAdmin, withdrawal, "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await WithdrawalHttp.ActAsync(world.TenantAdmin, withdrawal, "cancel");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        await ShouldBeCancelledAsync(world, id, "WithdrawalCancelled");
        var next = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        (await AccountClosureHttp.ListIdsAsync(world.TenantAdmin, world.AccountId)).ShouldBe([next, id]);
    }

    [Test]
    public async Task TheBankRejectingThePayment_CancelsTheClosure_PaymentFailed()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.AccountClosed));

        var withdrawal = await AccountClosureHttp.ApproveAndSendAsync(world, id);

        (await WithdrawalHttp.GetAsync(world.TenantAdmin, withdrawal)).GetProperty("status").GetString().ShouldBe("Failed");
        await ShouldBeCancelledAsync(world, id, "PaymentFailed");
    }

    [Test]
    public async Task AFailedAttempt_TheBankDidNotAnswer_KeepsTheClosureInProgress()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Unavailable));

        await AccountClosureHttp.ApproveAndSendAsync(world, id);

        (await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id)).GetProperty("status").GetString().ShouldBe("InProgress");
    }

    private static async Task ShouldBeCancelledAsync(WithdrawalWorld world, Guid id, string reason)
    {
        var item = await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("cancelReason").GetString().ShouldBe(reason);
        item.GetProperty("endedAt").ValueKind.ShouldBe(JsonValueKind.String);
        item.GetProperty("completedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        var account = await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId);
        account.GetProperty("status").GetString().ShouldBe("Open");
        account.GetProperty("cashBalance").GetDecimal().ShouldBe(1000m);
        account.GetProperty("closure").GetProperty("status").GetString().ShouldBe("Cancelled");
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(1);
    }
}
