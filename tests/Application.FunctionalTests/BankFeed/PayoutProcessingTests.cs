using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>Withdrawals R39–R43: the payout and the returned payout from the bank statement.</summary>
[Parallelizable]
public class PayoutProcessingTests : TestBase
{
    [Test]
    public async Task TheDebitLine_PostsThePayout_ReleasesTheReservation_AndPaysTheWithdrawal()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var (id, outsideId) = await world.PaidAsync(300m);

        var payout = (await PayoutsAsync(id)).ShouldHaveSingleItem();
        payout.Type.ShouldBe(TransactionType.TransferOut);
        payout.Memo.ShouldBe("Withdrawal");
        payout.Reference.ShouldBeNull();
        payout.CreatedBy.ShouldBe(SystemUser.Name);
        payout.BookedAt.ShouldBeGreaterThan(before);
        payout.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-300m);
        var detail = payout.PaymentDetail.ShouldNotBeNull();
        (detail.ValueDate, detail.Reference, detail.PayeeName, detail.PayeeAccount)
            .ShouldBe((DateOnly.FromDateTime(DateTime.UtcNow), world.DepositReference, "J Smith", "****8900"));
        (await world.CashAsync()).ShouldBe((700m, 0m, 700m));

        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Paid");
        item.GetProperty("payoutTransactionId").GetGuid().ShouldBe(payout.PublicId);
        item.GetProperty("payment").GetProperty("status").GetString().ShouldBe("Paid");
        var line = await BankFeedHttp.LineAsync(outsideId);
        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Payment);
        line.MatchedTransactionId.ShouldBe(payout.Id);
        line.WithdrawalId.ShouldNotBeNull();

        using var transaction = await CustodialHttp.GetTransactionAsync(world.TenantAdmin, payout.PublicId);
        var root = transaction.RootElement;
        root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("Payment");
        root.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(id);
        root.GetProperty("bankReceipt").ValueKind.ShouldBe(JsonValueKind.Null);
        var payment = root.GetProperty("payment");
        payment.GetProperty("valueDate").GetString().ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"));
        payment.GetProperty("reference").GetString().ShouldBe(world.DepositReference);
        payment.GetProperty("payeeName").GetString().ShouldBe("J Smith");
        payment.GetProperty("payeeAccount").GetString().ShouldBe("****8900");
    }

    [Test]
    public async Task APaymentWhoseAcceptedAnswerWasLost_IsPaidTheSameWay()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Timeout));
        await BankFeedHttp.SendAsync();
        world.AddPayoutLine(id, 300m);

        await BankFeedHttp.PullAsync();
        await BankFeedHttp.ProcessAsync();

        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Paid");
        (await PayoutsAsync(id)).ShouldHaveSingleItem();
        (await world.CashAsync()).ShouldBe((700m, 0m, 700m));
    }

    [Test]
    public async Task APayout_IsPostedEvenForADisabledCustomer()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);
        await BankFeedHttp.SendAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);
        world.AddPayoutLine(id, 300m);

        await BankFeedHttp.RunAsync();

        (await PayoutsAsync(id)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task AnotherAmount_LeavesTheLineUnmatched_AsAPaymentMismatch_AndPostsNothing()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);
        await BankFeedHttp.SendAsync();
        var outsideId = world.AddPayoutLine(id, 299.99m);

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(outsideId);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.PaymentMismatch);
        (await PayoutsAsync(id)).ShouldBeEmpty();
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Sent");
        (await world.CashAsync()).ShouldBe((1000m, 300m, 700m));
    }

    [Test]
    public async Task ProcessingTwice_OrASecondLineWithTheSameId_PostsOnePayout()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (id, _) = await world.PaidAsync(300m);
        await BankFeedHttp.RunAsync();
        var second = world.AddPayoutLine(id, 300m);

        await BankFeedHttp.RunAsync();

        (await PayoutsAsync(id)).ShouldHaveSingleItem();
        (await BankFeedHttp.LineAsync(second)).UnmatchedReason.ShouldBe(UnmatchedReason.PaymentMismatch);
        (await world.CashAsync()).ShouldBe((700m, 0m, 700m));
    }

    [Test]
    public async Task ADebitLineWithoutAWithdrawalOfTheTenant_IsAnUnknownPayment()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory");
        var southId = await south.ApprovedAsync(10m);
        var none = world.AddPayoutLine(Guid.Empty, 10m, endToEndId: "");
        var unknown = world.AddPayoutLine(Guid.NewGuid(), 10m);
        var notAGuid = world.AddPayoutLine(Guid.Empty, 10m, endToEndId: "not-a-guid");
        var otherTenants = world.AddPayoutLine(southId, 10m);

        await BankFeedHttp.RunAsync();

        foreach (var outsideId in new[] { none, unknown, notAGuid, otherTenants })
        {
            var line = await BankFeedHttp.LineAsync(outsideId);
            line.Status.ShouldBe(BankStatementLineStatus.Unmatched, outsideId);
            line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment, outsideId);
        }

        (await PayoutsAsync(southId)).ShouldBeEmpty();
    }

    [Test]
    public async Task AReturnLine_ReversesThePayout_AndReturnsTheWithdrawal()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (id, payoutLine) = await world.PaidAsync(300m);
        var returnLine = world.AddReturnLine(payoutLine, 300m, "Account closed at the payee's bank");

        await BankFeedHttp.RunAsync();

        var rows = await PayoutsAsync(id);
        rows.Count.ShouldBe(2);
        var reversal = rows.Single(row => row.Type == TransactionType.Reversal);
        reversal.CreatedBy.ShouldBe(SystemUser.Name);
        reversal.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(300m);
        reversal.PaymentDetail.ShouldNotBeNull().PayeeAccount.ShouldBe("****8900");
        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Returned");
        item.GetProperty("returnReason").GetString().ShouldBe("Account closed at the payee's bank");
        item.GetProperty("returnTransactionId").GetGuid().ShouldBe(reversal.PublicId);
        item.GetProperty("payment").GetProperty("status").GetString().ShouldBe("Returned");
        var line = await BankFeedHttp.LineAsync(returnLine);
        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.MatchMethod.ShouldBe(StatementLineMatchMethod.PaymentReturn);
        line.MatchedTransactionId.ShouldBe(reversal.Id);
        line.RelatedLineId.ShouldBe((await BankFeedHttp.LineAsync(payoutLine)).Id);
    }

    [Test]
    public async Task AReturnLine_ToAClosedAccount_IsUnmatched_AndPostsNothing()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (id, payoutLine) = await world.PaidAsync(1000m);
        (await CustodialHttp.CloseAsync(world.TenantAdmin, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var returnLine = world.AddReturnLine(payoutLine, 1000m);

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(returnLine);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.AccountClosed);
        (await PayoutsAsync(id)).ShouldHaveSingleItem();
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Paid");
    }

    [Test]
    public async Task AReturnLineOfAnotherAmount_IsAPaymentMismatch()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (id, payoutLine) = await world.PaidAsync(300m);
        var returnLine = world.AddReturnLine(payoutLine, 250m);

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(returnLine)).UnmatchedReason.ShouldBe(UnmatchedReason.PaymentMismatch);
        (await PayoutsAsync(id)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task AHandReverseOfAPayoutOrOfItsReversal_Returns400()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (id, payoutLine) = await world.PaidAsync(300m);
        var payout = (await PayoutsAsync(id)).Single();

        (await CustodialHttp.ReverseAsync(world.TenantAdmin, payout.PublicId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        world.AddReturnLine(payoutLine, 300m);
        await BankFeedHttp.RunAsync();
        var reversal = (await PayoutsAsync(id)).Single(row => row.Type == TransactionType.Reversal);
        (await CustodialHttp.ReverseAsync(world.TenantAdmin, reversal.PublicId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<List<Transaction>> PayoutsAsync(Guid withdrawalId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Include(row => row.CashLegs)
            .Include(row => row.PaymentDetail)
            .Where(row => row.SourceKind == TransactionSourceKind.Payment && row.SourcePublicId == withdrawalId)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }
}
