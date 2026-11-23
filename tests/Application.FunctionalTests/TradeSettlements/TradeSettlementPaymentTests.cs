using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R12, R17, R18, R21: the firm pays the broker the net of an agreed settlement, and the
/// bank's debit line completes it.</summary>
[Parallelizable]
public class TradeSettlementPaymentTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    private static FakeBrokerConnections Connections =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<FakeBrokerConnections>();

    [Test]
    public async Task ANetBelow0_CreatesAPendingTradeSettlementPayment_WithItsOwnInstructionId_AndTheReference()
    {
        var (world, clientMoney, settlement) = await PayingAsync();

        var payment = (await PaymentsAsync(settlement.Id)).ShouldHaveSingleItem();
        (payment.Kind, payment.Status, payment.Amount, payment.Currency, payment.Reference)
            .ShouldBe((OutgoingPaymentKind.TradeSettlement, OutgoingPaymentStatus.Pending, 400.48m, "USD", "w-S1"));
        (payment.TenantId, payment.ClientMoneyAccountId, payment.WithdrawalId, payment.ReturnOfLineId)
            .ShouldBe((world.Tenant.Id, clientMoney.Id, (int?)null, (int?)null));
        payment.InstructionPublicId.ShouldBe(payment.PublicId);
        (await SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.AwaitingPayment);
    }

    [Test]
    public async Task TheSendStep_PaysTheConfiguredBrokerAccount_AndAcceptedLeavesTheSettlementAwaitingPayment()
    {
        var (_, clientMoney, settlement) = await PayingAsync();

        await BankFeedHttp.SendAsync();

        var request = BankFeedHttp.Payments.Requests.ShouldHaveSingleItem();
        (request.TrustAccountNumber, request.Amount, request.Currency, request.Reference)
            .ShouldBe((clientMoney.AccountNumber, 400.48m, "USD", "w-S1"));
        request.Payee.ShouldBe(new BankPaymentPayee("BRK-SIM-USD", "Simulator Broker"));
        request.ReturnOfOutsideId.ShouldBeNull();
        var payment = (await PaymentsAsync(settlement.Id)).Single();
        (payment.Status, payment.Attempts).ShouldBe((OutgoingPaymentStatus.Accepted, 1));
        request.InstructionId.ShouldBe(payment.InstructionPublicId);
        (await SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.AwaitingPayment);
    }

    [Test]
    public async Task TheDebitLine_WithTheInstructionIdAndTheAmount_MatchesAsSettlement_PaysThePayment_AndCompletesTheSettlement()
    {
        var (_, clientMoney, settlement) = await PayingAsync();
        await BankFeedHttp.SendAsync();
        var payment = (await PaymentsAsync(settlement.Id)).Single();
        var debit = BankFeedHttp.Feed.Add(clientMoney.AccountNumber, Debit(400.48m, payment.InstructionPublicId));

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(debit.Id!);
        (line.Status, line.MatchMethod, line.TradeSettlementId, line.MatchedAccountId, line.MatchedTransactionId).ShouldBe((
            BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.Settlement, (int?)settlement.Id,
            (int?)null, (int?)null));
        (await PaymentsAsync(settlement.Id)).Single().Status.ShouldBe(OutgoingPaymentStatus.Paid);
        var completed = await SettlementAsync(settlement.Id);
        (completed.Status, completed.CompletedAt is not null).ShouldBe((TradeSettlementStatus.Completed, true));
    }

    [Test]
    public async Task ADebitLineOfAnotherAmount_IsUnmatchedAsPaymentMismatch_AndTheSettlementWaits()
    {
        var (_, clientMoney, settlement) = await PayingAsync();
        await BankFeedHttp.SendAsync();
        var payment = (await PaymentsAsync(settlement.Id)).Single();
        var debit = BankFeedHttp.Feed.Add(clientMoney.AccountNumber, Debit(400.47m, payment.InstructionPublicId));

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(debit.Id!);
        (line.Status, line.UnmatchedReason, line.TradeSettlementId).ShouldBe((
            BankStatementLineStatus.Unmatched, (UnmatchedReason?)UnmatchedReason.PaymentMismatch, (int?)null));
        (await PaymentsAsync(settlement.Id)).Single().Status.ShouldBe(OutgoingPaymentStatus.Accepted);
        (await SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.AwaitingPayment);
    }

    [Test]
    public async Task ADebitLineForAPaymentNeverSent_IsAPaymentMismatch()
    {
        var (_, clientMoney, settlement) = await PayingAsync();
        var payment = (await PaymentsAsync(settlement.Id)).Single();
        var debit = BankFeedHttp.Feed.Add(clientMoney.AccountNumber, Debit(400.48m, payment.InstructionPublicId));

        await BankFeedHttp.PullAsync();
        await BankFeedHttp.ProcessAsync();

        (await BankFeedHttp.LineAsync(debit.Id!)).UnmatchedReason.ShouldBe(UnmatchedReason.PaymentMismatch);
        (await SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.AwaitingPayment);
    }

    [TestCase(PaymentRejectReason.InvalidAccount)]
    [TestCase(PaymentRejectReason.Refused)]
    public async Task ARejectedPayment_FailsTheSettlement_WithTheReason(PaymentRejectReason reason)
    {
        var (_, _, settlement) = await PayingAsync();
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(reason));

        await BankFeedHttp.SendAsync();

        (await PaymentsAsync(settlement.Id)).Single().Status.ShouldBe(OutgoingPaymentStatus.Rejected);
        var failed = await SettlementAsync(settlement.Id);
        (failed.Status, failed.Failure, failed.FailureDetail).ShouldBe((
            TradeSettlementStatus.PaymentFailed, (TradeSettlementFailure?)TradeSettlementFailure.PaymentRejected, reason.ToString()));
    }

    [Test]
    public async Task AFailedCall_LeavesThePaymentPending_ForTheNextRun()
    {
        var (_, _, settlement) = await PayingAsync();
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Timeout));

        await BankFeedHttp.SendAsync();
        await BankFeedHttp.SendAsync();

        BankFeedHttp.Payments.Requests.Count.ShouldBe(2);
        BankFeedHttp.Payments.Requests.Select(request => request.InstructionId).Distinct().Count().ShouldBe(1);
        (await PaymentsAsync(settlement.Id)).Single().Status.ShouldBe(OutgoingPaymentStatus.Accepted);
    }

    [Test]
    public async Task WithNoBrokerAccountWhenTheSettlementIsProcessed_NoPaymentIsMade_AndItFailsWithNoBrokerAccount()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        var (id, fill) = await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);
        Connections.Reset();
        Feed.AddSettled(id, fill, settlementId: "w-S1");
        Feed.AddSettlement("w-S1", clientMoney.AccountNumber, -400.48m, 1);

        await BrokerHttp.RunReportsAsync();

        var settlement = (await TradeSettlementReportsTests.SettlementsAsync(world.Tenant.Id)).Single();
        (settlement.Status, settlement.Failure).ShouldBe((
            TradeSettlementStatus.PaymentFailed, (TradeSettlementFailure?)TradeSettlementFailure.NoBrokerAccount));
        (await PaymentsAsync(settlement.Id)).ShouldBeEmpty();
    }

    /// <summary>A buy of 4 at 100.12, settled in <c>w-S1</c> with the same figures: the firm owes 400.48.</summary>
    internal static async Task<(Orders.OrderWorld World, ClientMoneyAccount ClientMoney, TradeSettlement Settlement)> PayingAsync()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        var (id, fill) = await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);
        Feed.AddSettled(id, fill, settlementId: "w-S1");
        Feed.AddSettlement("w-S1", clientMoney.AccountNumber, -400.48m, 1);
        await BrokerHttp.RunReportsAsync();
        return (world, clientMoney, (await TradeSettlementReportsTests.SettlementsAsync(world.Tenant.Id)).Single());
    }

    internal static BankFeedLine Debit(decimal amount, Guid instruction) => new(
        "fake-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, amount, "USD",
        "SIMULATOR BROKER", "BRK-SIM-USD", "w-S1", EndToEndId: instruction.ToString());

    internal static async Task<List<OutgoingPayment>> PaymentsAsync(int settlementId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TradeSettlementId == settlementId)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }

    internal static async Task<TradeSettlement> SettlementAsync(int id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.TradeSettlements.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == id);
    }
}
