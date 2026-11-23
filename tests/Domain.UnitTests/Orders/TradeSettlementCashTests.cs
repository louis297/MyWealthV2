using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Orders;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Trade-settlement R12, R18, R19 (§5): the settlement payment and the statement line matched to a trade
/// settlement.</summary>
public class TradeSettlementCashTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 2, 0, 4, TimeSpan.Zero);

    [Test]
    public void ForTradeSettlement_IsPending_ForMinusTheNet_WithItsOwnInstructionId_AndTheSettlementIdAsReference()
    {
        var settlement = Settlement(-1001.50m);

        var payment = OutgoingPayment.ForTradeSettlement(settlement, ClientMoney());

        (payment.TenantId, payment.ClientMoneyAccountId, payment.Connection, payment.Kind)
            .ShouldBe((7, 40, "simulator", OutgoingPaymentKind.TradeSettlement));
        (payment.TradeSettlementId, payment.WithdrawalId, payment.ReturnOfLineId).ShouldBe(((int?)60, (int?)null, (int?)null));
        (payment.Amount, payment.Currency, payment.Reference, payment.Status)
            .ShouldBe((1001.50m, "USD", "0f8fad5b-S3", OutgoingPaymentStatus.Pending));
        payment.InstructionPublicId.ShouldBe(payment.PublicId);
        payment.PublicId.ShouldNotBe(settlement.PublicId);
    }

    [Test]
    public void ForTradeSettlement_LeavesOutAReferenceThatIsNotAValidPaymentReference()
    {
        OutgoingPayment.ForTradeSettlement(Settlement(-1001.50m, "0f8fad5b_S3"), ClientMoney()).Reference.ShouldBeNull();
        OutgoingPayment.ForTradeSettlement(Settlement(-1001.50m, "0f8fad5bd9cb469fa1-S3"), ClientMoney()).Reference.ShouldBeNull();
    }

    [Test]
    public void ForTradeSettlement_RefusesASettlementThatDoesNotAwaitPayment_AnUnsavedOne_AndAnotherClientMoneyAccount()
    {
        Should.Throw<DomainException>(() => OutgoingPayment.ForTradeSettlement(Settlement(2340m), ClientMoney()));
        var unsaved = Settlement(-1001.50m);
        unsaved.Id = 0;
        Should.Throw<DomainException>(() => OutgoingPayment.ForTradeSettlement(unsaved, ClientMoney()));
        var other = ClientMoney();
        other.Id = 41;
        Should.Throw<DomainException>(() => OutgoingPayment.ForTradeSettlement(Settlement(-1001.50m), other));
    }

    [TestCase(BankLineDirection.Debit)]
    [TestCase(BankLineDirection.Credit)]
    public void MarkSettlement_MatchesTheLine_WithNoAccountOrTransaction(BankLineDirection direction)
    {
        var line = Line(direction);

        line.MarkSettlement(60, At);

        (line.Status, line.MatchMethod, line.MatchedAt).ShouldBe((
            BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.Settlement, (DateTimeOffset?)At));
        (line.TradeSettlementId, line.MatchedAccountId, line.MatchedTransactionId).ShouldBe(((int?)60, (int?)null, (int?)null));
    }

    [Test]
    public void MarkSettlement_OnlyFromReceived()
    {
        var line = Line(BankLineDirection.Debit);
        line.MarkSettlement(60, At);

        Should.Throw<DomainException>(() => line.MarkSettlement(60, At));
    }

    private static BankStatementLine Line(BankLineDirection direction)
    {
        var line = BankStatementLine.Receive(
            7, 40, "simulator", "w-L8", new DateOnly(2026, 11, 25), direction, 1001.50m, "USD", "SIMULATOR BROKER",
            "****0001", "0f8fad5b-S3", At);
        line.Id = 50;
        return line;
    }

    private static TradeSettlement Settlement(decimal net, string settlementId = "0f8fad5b-S3")
    {
        var side = net < 0 ? OrderSide.Buy : OrderSide.Sell;
        var settlement = TradeSettlement.Create(
            ClientMoney(), "simulator", settlementId, new DateOnly(2026, 11, 25), 90L,
            [new SettlementFill(side, 1m, Math.Abs(net))], 1, net, "SIM-DEMO-USD", "USD", true, At);
        settlement.Id = 60;
        return settlement;
    }

    private static ClientMoneyAccount ClientMoney()
    {
        var account = ClientMoneyAccount.Create(
            7, Currency.Create("USD", "USD", 2), "Simulator Bank", "Trust", "SIM-DEMO-USD", "simulator");
        account.Id = 40;
        return account;
    }
}
