using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R14, R20: a settlement payment the broker's bank returns fails the settlement, and a
/// TenantAdmin pays a failed settlement again.</summary>
[Parallelizable]
public class TradeSettlementPayAgainTests : TestBase
{
    private static FakeBrokerConnections Connections =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<FakeBrokerConnections>();

    [Test]
    public async Task AReturnedSettlementPayment_MatchesAsPaymentReturn_ReturnsThePayment_AndFailsTheSettlement()
    {
        var (_, clientMoney, settlement, debit) = await PaidAsync();
        var credit = BankFeedHttp.Feed.Add(clientMoney.AccountNumber, Return(debit, 400.48m));

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(credit.Id!);
        var returned = await BankFeedHttp.LineAsync(debit);
        (line.Status, line.MatchMethod, line.TradeSettlementId, line.RelatedLineId, line.MatchedAccountId).ShouldBe((
            BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.PaymentReturn, (int?)settlement.Id,
            (int?)returned.Id, (int?)null));
        (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id)).Single().Status.ShouldBe(OutgoingPaymentStatus.Returned);
        var failed = await TradeSettlementPaymentTests.SettlementAsync(settlement.Id);
        (failed.Status, failed.Failure, failed.FailureDetail, failed.CompletedAt).ShouldBe((
            TradeSettlementStatus.PaymentFailed, (TradeSettlementFailure?)TradeSettlementFailure.PaymentReturned, "AccountClosed",
            (DateTimeOffset?)null));
    }

    [Test]
    public async Task AReturnOfAnotherAmount_IsAPaymentMismatch_AndTheSettlementStaysCompleted()
    {
        var (_, clientMoney, settlement, debit) = await PaidAsync();
        var credit = BankFeedHttp.Feed.Add(clientMoney.AccountNumber, Return(debit, 400.00m));

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(credit.Id!)).UnmatchedReason.ShouldBe(UnmatchedReason.PaymentMismatch);
        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.Completed);
    }

    [Test]
    public async Task PayAgain_AfterARejection_Returns204_AndCreatesASecondPaymentWithANewInstructionId()
    {
        var (world, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.InvalidAccount));
        await BankFeedHttp.SendAsync();
        var failed = await TradeSettlementPaymentTests.SettlementAsync(settlement.Id);

        var response = await PayAsync(world.TenantAdmin, failed);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var payments = await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id);
        payments.Select(payment => payment.Status).ShouldBe([OutgoingPaymentStatus.Rejected, OutgoingPaymentStatus.Pending]);
        payments[1].InstructionPublicId.ShouldNotBe(payments[0].InstructionPublicId);
        (payments[1].Amount, payments[1].Reference).ShouldBe((400.48m, "w-S1"));
        var paying = await TradeSettlementPaymentTests.SettlementAsync(settlement.Id);
        (paying.Status, paying.Failure).ShouldBe((TradeSettlementStatus.AwaitingPayment, (TradeSettlementFailure?)null));
    }

    [Test]
    public async Task PayAgain_AfterAReturn_PaysAgain_AndTheNewDebitLineCompletesTheSettlement()
    {
        var (world, clientMoney, settlement, debit) = await PaidAsync();
        BankFeedHttp.Feed.Add(clientMoney.AccountNumber, Return(debit, 400.48m));
        await BankFeedHttp.RunAsync();

        (await PayAsync(world.TenantAdmin, await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await BankFeedHttp.SendAsync();
        var second = (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id))[1];
        BankFeedHttp.Feed.Add(clientMoney.AccountNumber, TradeSettlementPaymentTests.Debit(400.48m, second.InstructionPublicId));
        await BankFeedHttp.RunAsync();

        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.Completed);
        (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id)).Select(payment => payment.Status)
            .ShouldBe([OutgoingPaymentStatus.Returned, OutgoingPaymentStatus.Paid]);
    }

    [Test]
    public async Task PayAgain_FromAnyOtherStatus_Returns400AtStatus()
    {
        var (world, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();

        var response = await PayAsync(world.TenantAdmin, settlement);

        (await CustodialHttp.ErrorsAsync(response))["Status"]
            .ShouldBe(["This settlement is AwaitingPayment and cannot be paid again."]);
        (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task PayAgain_WithNoBrokerAccountOnTheHost_Returns400AtStatus()
    {
        var (world, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.Refused));
        await BankFeedHttp.SendAsync();
        Connections.Reset();

        var response = await PayAsync(world.TenantAdmin, await TradeSettlementPaymentTests.SettlementAsync(settlement.Id));

        (await CustodialHttp.ErrorsAsync(response))["Status"].ShouldBe(["The broker connection has no bank account in USD."]);
        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.PaymentFailed);
    }

    [Test]
    public async Task PayAgain_WithAStaleRowVersion_Returns409_AndWithoutOne_Returns400()
    {
        var (world, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.Refused));
        await BankFeedHttp.SendAsync();

        var stale = await TenantHttp.Send(
            HttpMethod.Post, $"/trade-settlements/{settlement.PublicId}/pay", world.TenantAdmin,
            new { rowVersion = Convert.ToBase64String(settlement.RowVersion) });
        var missing = await TenantHttp.Send(
            HttpMethod.Post, $"/trade-settlements/{settlement.PublicId}/pay", world.TenantAdmin, new { });

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).ShouldContain("stale_row_version");
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustodialHttp.ErrorsAsync(missing)).ShouldContainKey("RowVersion");
    }

    internal static Task<HttpResponseMessage> PayAsync(string accessToken, TradeSettlement settlement) =>
        TenantHttp.Send(
            HttpMethod.Post, $"/trade-settlements/{settlement.PublicId}/pay", accessToken,
            new { rowVersion = Convert.ToBase64String(settlement.RowVersion) });

    /// <summary>The firm's 400.48 sent and on the statement: the settlement is <c>Completed</c>.</summary>
    private static async Task<(Orders.OrderWorld World, ClientMoneyAccount ClientMoney, TradeSettlement Settlement, string DebitId)> PaidAsync()
    {
        var (world, clientMoney, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        await BankFeedHttp.SendAsync();
        var payment = (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id)).Single();
        var debit = BankFeedHttp.Feed.Add(clientMoney.AccountNumber, TradeSettlementPaymentTests.Debit(400.48m, payment.InstructionPublicId));
        await BankFeedHttp.RunAsync();
        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.Completed);
        return (world, clientMoney, settlement, debit.Id!);
    }

    private static BankFeedLine Return(string debitId, decimal amount) => new(
        "fake-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Credit, amount, "USD",
        "SIMULATOR BROKER", "BRK-SIM-USD", null, ReturnOf: debitId, ReturnReason: "AccountClosed");
}
