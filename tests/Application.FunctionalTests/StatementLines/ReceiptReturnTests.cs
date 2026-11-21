using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>Bank-returns R5–R7, R11: request return creates the return payment, reopen cancels it until the first
/// attempt, the send step sends it, and the bank's debit line closes it.</summary>
[Parallelizable]
public class ReceiptReturnTests : TestBase
{
    private const string ReopenRefused = "The bank has been asked to return this receipt, so it can no longer be reopened.";

    [Test]
    public async Task RequestReturn_CreatesAPendingReceiptReturn_WithItsOwnInstructionId()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(250.50m, "Wrong firm");

        var response = await RequestReturnAsync(world, line);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        payment.Kind.ShouldBe(OutgoingPaymentKind.ReceiptReturn);
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
        payment.Attempts.ShouldBe(0);
        payment.WithdrawalId.ShouldBeNull();
        payment.Reference.ShouldBeNull();
        payment.Amount.ShouldBe(250.50m);
        payment.Currency.Trim().ShouldBe("USD");
        payment.TenantId.ShouldBe(world.Tenant.Id);
        payment.ClientMoneyAccountId.ShouldBe(world.ClientMoney.Id);
        payment.Connection.ShouldBe(BankFeedHttp.Connection);
        payment.InstructionPublicId.ShouldBe(payment.PublicId);
        payment.InstructionPublicId.ShouldNotBe(line.PublicId);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnRequested);
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        ReturnPayment(item).ShouldBe(("Pending", 0, null));
    }

    [Test]
    public async Task AReceiptWithNoReturn_HasNoReturnPayment_OnTheItem()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);

        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);

        item.GetProperty("returnPayment").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Reopen_BeforeTheFirstAttempt_Returns204_AndCancelsThePayment()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(20m, null);
        await RequestReturnAsync(world, line);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "reopen");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
        (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Cancelled);

        await BankFeedHttp.SendAsync();
        BankFeedHttp.Payments.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task Reopen_AfterTheFirstAttempt_Returns400AtStatus_AndChangesNothing()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(20m, null);
        await RequestReturnAsync(world, line);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Unavailable));
        await BankFeedHttp.SendAsync();

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "reopen");

        (await CustodialHttp.ErrorsAsync(response))["Status"].ShouldBe([ReopenRefused]);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnRequested);
        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
        payment.Attempts.ShouldBe(1);
    }

    [Test]
    public async Task Send_CallsThePortWithTheReturnedLine_AndNoPayee_AndAcceptedMakesTheLineReturnSent()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        await RequestReturnAsync(world, line);
        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();

        await BankFeedHttp.SendAsync();

        var request = BankFeedHttp.Payments.Requests.ShouldHaveSingleItem();
        request.InstructionId.ShouldBe(payment.InstructionPublicId);
        request.ReturnOfOutsideId.ShouldBe(line.OutsideId);
        request.Payee.ShouldBeNull();
        request.Reference.ShouldBeNull();
        request.Amount.ShouldBe(75m);
        request.Currency.ShouldBe("USD");
        request.TrustAccountNumber.ShouldBe(world.ClientMoney.AccountNumber);
        (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Accepted);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnSent);
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        item.GetProperty("status").GetString().ShouldBe("ReturnSent");
        ReturnPayment(item).ShouldBe(("Accepted", 1, null));
    }

    [Test]
    public async Task Rejected_PutsTheLineBackInSuspense_AndASecondRequestCreatesANewPaymentWithANewInstructionId()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        await RequestReturnAsync(world, line);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.UnknownLine));

        await BankFeedHttp.SendAsync();

        var rejected = (await BankFeedHttp.LineAsync(line.OutsideId));
        rejected.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        rejected.UnmatchedReason.ShouldBe(UnmatchedReason.ReturnRejected);
        var first = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        first.Status.ShouldBe(OutgoingPaymentStatus.Rejected);
        first.RejectReason.ShouldBe(PaymentRejectReason.UnknownLine);

        var again = await RequestReturnAsync(world, line);

        again.StatusCode.ShouldBe(HttpStatusCode.NoContent, await again.Content.ReadAsStringAsync());
        var payments = await ReturnPaymentsAsync(line.Id);
        payments.Count.ShouldBe(2);
        payments[1].Status.ShouldBe(OutgoingPaymentStatus.Pending);
        payments[1].InstructionPublicId.ShouldNotBe(first.InstructionPublicId);
        await BankFeedHttp.SendAsync();
        BankFeedHttp.Payments.Requests.Select(request => request.InstructionId)
            .ShouldBe([first.InstructionPublicId, payments[1].InstructionPublicId]);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnSent);
    }

    [Test]
    public async Task AFailedCall_LeavesThePaymentPending_AndTheLineReturnRequested()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        await RequestReturnAsync(world, line);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Unavailable));

        await BankFeedHttp.SendAsync();

        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
        payment.LastFailure.ShouldBe(BankFeedFailure.Unavailable);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnRequested);
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        ReturnPayment(item).ShouldBe(("Pending", 1, "Unavailable"));
    }

    [Test]
    public async Task TheBanksDebitLine_ReturnsTheReceipt_AndWritesNothingToTheLedger()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        await RequestReturnAsync(world, line);
        await BankFeedHttp.SendAsync();
        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        var debitId = AddReturnDebit(world, payment.InstructionPublicId.ToString(), line.OutsideId, 75m);

        await BankFeedHttp.RunAsync();

        await ShouldBeReturnedAsync(world, line, debitId);
    }

    [Test]
    public async Task TheBanksDebitLine_WithTheAcceptedAnswerLost_ReturnsTheReceiptTheSameWay()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        await RequestReturnAsync(world, line);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Timeout));
        await BankFeedHttp.SendAsync();
        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
        var debitId = AddReturnDebit(world, payment.InstructionPublicId.ToString(), line.OutsideId, 75m);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Timeout));

        await BankFeedHttp.RunAsync();

        await ShouldBeReturnedAsync(world, line, debitId);
    }

    [Test]
    public async Task TheBanksDebitLine_OfAnotherAmount_IsAPaymentMismatch_AndTheReceiptStaysReturnSent()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        await RequestReturnAsync(world, line);
        await BankFeedHttp.SendAsync();
        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        var debitId = AddReturnDebit(world, payment.InstructionPublicId.ToString(), line.OutsideId, 70m);

        await BankFeedHttp.RunAsync();

        var debit = await BankFeedHttp.LineAsync(debitId);
        debit.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        debit.UnmatchedReason.ShouldBe(UnmatchedReason.PaymentMismatch);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnSent);
        (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Accepted);
    }

    [Test]
    public async Task ADebitNamingAReturnNeverSent_IsAnUnknownPayment()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        await RequestReturnAsync(world, line);
        var payment = (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem();
        var debitId = AddReturnDebit(world, payment.InstructionPublicId.ToString(), null, 75m);

        await BankFeedHttp.PullAsync();
        await BankFeedHttp.ProcessAsync();

        (await BankFeedHttp.LineAsync(debitId)).UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnRequested);
    }

    [Test]
    public async Task ADebitNamingAnotherTenantsReturnPayment_IsAnUnknownPayment()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var world = await DepositMatchingWorld.CreateAsync();
        var southLine = await south.ReceiveAsync(75m, null);
        await RequestReturnAsync(south, southLine);
        await BankFeedHttp.SendAsync();
        var southPayment = (await ReturnPaymentsAsync(southLine.Id)).ShouldHaveSingleItem();
        var debitId = AddReturnDebit(world, southPayment.InstructionPublicId.ToString(), null, 75m);

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(debitId)).UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
        (await BankFeedHttp.LineAsync(southLine.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnSent);
        (await ReturnPaymentsAsync(southLine.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Accepted);
    }

    private static async Task ShouldBeReturnedAsync(DepositMatchingWorld world, BankStatementLine line, string debitId)
    {
        var receipt = await BankFeedHttp.LineAsync(line.OutsideId);
        receipt.Status.ShouldBe(BankStatementLineStatus.Returned);
        receipt.MatchedAccountId.ShouldBeNull();
        (await ReturnPaymentsAsync(line.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Paid);
        var debit = await BankFeedHttp.LineAsync(debitId);
        debit.Status.ShouldBe(BankStatementLineStatus.Matched);
        debit.MatchMethod.ShouldBe(StatementLineMatchMethod.Payment);
        debit.MatchedAccountId.ShouldBeNull();
        debit.MatchedTransactionId.ShouldBeNull();
        debit.RelatedLineId.ShouldBe(line.Id);
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        (await BankFeedHttp.TransactionsOfLineAsync(debit.PublicId)).ShouldBeEmpty();
        (await world.CashBalanceAsync()).ShouldBe(0m);

        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, debit.PublicId);
        item.GetProperty("relatedLineId").GetGuid().ShouldBe(line.PublicId);
        var match = item.GetProperty("match");
        match.GetProperty("method").GetString().ShouldBe("Payment");
        match.GetProperty("accountId").ValueKind.ShouldBe(JsonValueKind.Null);
        match.GetProperty("customerId").ValueKind.ShouldBe(JsonValueKind.Null);
        match.GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        var returned = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        returned.GetProperty("status").GetString().ShouldBe("Returned");
        returned.GetProperty("match").ValueKind.ShouldBe(JsonValueKind.Null);
        ReturnPayment(returned).Status.ShouldBe("Paid");
    }

    private static Task<HttpResponseMessage> RequestReturnAsync(DepositMatchingWorld world, BankStatementLine line) =>
        StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "request-return", new() { ["note"] = "Not our customer" });

    /// <summary>The bank's debit line of an executed return (bank-returns R13).</summary>
    private static string AddReturnDebit(DepositMatchingWorld world, string endToEndId, string? returnOf, decimal amount)
    {
        var id = "return-debit-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            id, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, amount, "USD", "J SMITH", "12-3456-7890042-00",
            null, EndToEndId: endToEndId, ReturnOf: returnOf));
        return id;
    }

    private static (string Status, int Attempts, string? LastFailure) ReturnPayment(JsonElement item)
    {
        var payment = item.GetProperty("returnPayment");
        var failure = payment.GetProperty("lastFailure");
        return (payment.GetProperty("status").GetString()!, payment.GetProperty("attempts").GetInt32(),
            failure.ValueKind == JsonValueKind.Null ? null : failure.GetString());
    }

    internal static async Task<List<OutgoingPayment>> ReturnPaymentsAsync(int lineId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.ReturnOfLineId == lineId)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }
}
