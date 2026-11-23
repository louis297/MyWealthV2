using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Orders;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Trade-settlement R9–R15 (docs/features/trade-settlement.md §5): the trade settlement and its moves.</summary>
public class TradeSettlementTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 2, 0, 4, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 25);

    // A buy of 4 at 100.12 (400.48) and of 6 at 100.17 (601.02): the firm pays 1001.50.
    private static readonly SettlementFill[] Buys =
    [
        new(OrderSide.Buy, 4m, 100.12m),
        new(OrderSide.Buy, 6m, 100.17m)
    ];

    [Test]
    public void Cash_IsTheRoundedGross_PlusForASellAndMinusForABuy()
    {
        new SettlementFill(OrderSide.Buy, 4m, 100.12m).Cash.ShouldBe(-400.48m);
        new SettlementFill(OrderSide.Sell, 3m, 33.33335m).Cash.ShouldBe(100.0001m);
        new SettlementFill(OrderSide.Sell, 1m, 0.00005m).Cash.ShouldBe(0.0001m);
    }

    [Test]
    public void Create_WithTheSameFigures_AndANetBelow0_AwaitsPayment()
    {
        var settlement = Create(Buys, brokerFillCount: 2, brokerNet: -1001.50m);

        settlement.TenantId.ShouldBe(7);
        settlement.ClientMoneyAccountId.ShouldBe(40);
        settlement.Connection.ShouldBe("simulator");
        settlement.BrokerSettlementId.ShouldBe("0f8fad5b-S3");
        settlement.Currency.ShouldBe("USD");
        settlement.SettledDate.ShouldBe(Day);
        settlement.ExecutionReportId.ShouldBe(90L);
        (settlement.FillCount, settlement.NetAmount).ShouldBe((2, -1001.50m));
        (settlement.BrokerFillCount, settlement.BrokerNetAmount, settlement.BrokerSettlementAccount)
            .ShouldBe((2, -1001.50m, "SIM-DEMO-USD"));
        settlement.Status.ShouldBe(TradeSettlementStatus.AwaitingPayment);
        settlement.Failure.ShouldBeNull();
        settlement.CompletedAt.ShouldBeNull();
        settlement.IsOpen.ShouldBeTrue();
        settlement.PublicId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public void Create_WithTheSameFigures_AndANetAbove0_AwaitsTheReceipt()
    {
        var settlement = Create([new(OrderSide.Sell, 10m, 234m)], brokerFillCount: 1, brokerNet: 2340m);

        settlement.Status.ShouldBe(TradeSettlementStatus.AwaitingReceipt);
        settlement.NetAmount.ShouldBe(2340m);
        settlement.IsOpen.ShouldBeTrue();
    }

    [Test]
    public void Create_WithANetOf0_IsCompletedAtOnce()
    {
        var settlement = Create(
            [new(OrderSide.Buy, 2m, 50m), new(OrderSide.Sell, 1m, 100m)], brokerFillCount: 2, brokerNet: 0m);

        settlement.Status.ShouldBe(TradeSettlementStatus.Completed);
        settlement.CompletedAt.ShouldBe(At);
        settlement.IsOpen.ShouldBeFalse();
    }

    [Test]
    public void Create_ComparesTheSettlementAccountWithoutCaseSpacesAndHyphens()
    {
        Create(Buys, brokerFillCount: 2, brokerNet: -1001.50m, brokerAccount: " sim demo-usd ")
            .Status.ShouldBe(TradeSettlementStatus.AwaitingPayment);
    }

    [TestCase(3, -1001.50, "SIM-DEMO-USD", "USD")]
    [TestCase(2, -1001.49, "SIM-DEMO-USD", "USD")]
    [TestCase(2, -1001.50, "SIM-DEMO-NZD", "USD")]
    [TestCase(2, -1001.50, "SIM-DEMO-USD", "NZD")]
    public void Create_WithOtherFigures_IsDisputed_AndKeepsBothSets(
        int brokerFillCount, decimal brokerNet, string brokerAccount, string brokerCurrency)
    {
        var settlement = Create(Buys, brokerFillCount, brokerNet, brokerAccount, brokerCurrency);

        settlement.Status.ShouldBe(TradeSettlementStatus.Disputed);
        (settlement.FillCount, settlement.NetAmount).ShouldBe((2, -1001.50m));
        (settlement.BrokerFillCount, settlement.BrokerNetAmount, settlement.BrokerSettlementAccount)
            .ShouldBe((brokerFillCount, brokerNet, brokerAccount));
        settlement.IsOpen.ShouldBeTrue();
    }

    [Test]
    public void Create_AgreedBelow0_WithNoBrokerAccount_FailsWithNoBrokerAccount()
    {
        var settlement = Create(Buys, brokerFillCount: 2, brokerNet: -1001.50m, configured: false);

        settlement.Status.ShouldBe(TradeSettlementStatus.PaymentFailed);
        settlement.Failure.ShouldBe(TradeSettlementFailure.NoBrokerAccount);
    }

    [Test]
    public void Create_AgreedAbove0_WithNoBrokerAccount_StillAwaitsTheReceipt()
    {
        Create([new(OrderSide.Sell, 10m, 234m)], brokerFillCount: 1, brokerNet: 2340m, configured: false)
            .Status.ShouldBe(TradeSettlementStatus.AwaitingReceipt);
    }

    [Test]
    public void Create_RefusesNoFills_AndABadSettlementId()
    {
        Should.Throw<DomainException>(() => Create([], brokerFillCount: 0, brokerNet: 0m));
        Should.Throw<DomainException>(() => Create(Buys, 2, -1001.50m, settlementId: ""));
        Should.Throw<DomainException>(() => Create(Buys, 2, -1001.50m, settlementId: new string('S', 101)));
    }

    [Test]
    public void Complete_FromAwaitingPaymentOrReceipt_IsCompleted()
    {
        var paid = Create(Buys, 2, -1001.50m);
        paid.Complete(At.AddHours(1));
        (paid.Status, paid.CompletedAt).ShouldBe((TradeSettlementStatus.Completed, (DateTimeOffset?)At.AddHours(1)));

        var received = Create([new(OrderSide.Sell, 10m, 234m)], 1, 2340m);
        received.Complete(At.AddHours(1));
        received.Status.ShouldBe(TradeSettlementStatus.Completed);
    }

    [TestCase(TradeSettlementStatus.PaymentFailed)]
    [TestCase(TradeSettlementStatus.Disputed)]
    [TestCase(TradeSettlementStatus.SetAside)]
    [TestCase(TradeSettlementStatus.Completed)]
    public void Complete_FromAnyOtherStatus_Throws(TradeSettlementStatus status)
    {
        Should.Throw<TradeSettlementStatusException>(() => InStatus(status).Complete(At));
    }

    [Test]
    public void FailPayment_FromAwaitingPayment_KeepsTheFailureAndTheDetail()
    {
        var settlement = Create(Buys, 2, -1001.50m);

        settlement.FailPayment(TradeSettlementFailure.PaymentRejected, "InvalidAccount");

        (settlement.Status, settlement.Failure, settlement.FailureDetail).ShouldBe((
            TradeSettlementStatus.PaymentFailed, (TradeSettlementFailure?)TradeSettlementFailure.PaymentRejected,
            "InvalidAccount"));
        settlement.IsOpen.ShouldBeTrue();
    }

    [Test]
    public void FailPayment_Returned_FromCompleted_ReopensIt()
    {
        var settlement = Create(Buys, 2, -1001.50m);
        settlement.Complete(At);

        settlement.FailPayment(TradeSettlementFailure.PaymentReturned, "AccountClosed");

        (settlement.Status, settlement.Failure, settlement.CompletedAt).ShouldBe((
            TradeSettlementStatus.PaymentFailed, (TradeSettlementFailure?)TradeSettlementFailure.PaymentReturned,
            (DateTimeOffset?)null));
    }

    [Test]
    public void FailPayment_RefusesAReceiptSettlement_ARejectionOfACompletedOne_AndADetailTooLong()
    {
        Should.Throw<TradeSettlementStatusException>(() =>
            Create([new(OrderSide.Sell, 10m, 234m)], 1, 2340m).FailPayment(TradeSettlementFailure.PaymentRejected, null));
        var completed = Create(Buys, 2, -1001.50m);
        completed.Complete(At);
        Should.Throw<TradeSettlementStatusException>(() =>
            completed.FailPayment(TradeSettlementFailure.PaymentRejected, null));
        Should.Throw<DomainException>(() =>
            Create(Buys, 2, -1001.50m).FailPayment(TradeSettlementFailure.PaymentRejected, new string('x', 141)));
    }

    [Test]
    public void PayAgain_FromPaymentFailed_AwaitsPayment_AndClearsTheFailure()
    {
        var settlement = InStatus(TradeSettlementStatus.PaymentFailed);

        settlement.PayAgain();

        (settlement.Status, settlement.Failure, settlement.FailureDetail)
            .ShouldBe((TradeSettlementStatus.AwaitingPayment, (TradeSettlementFailure?)null, (string?)null));
    }

    [TestCase(TradeSettlementStatus.AwaitingPayment)]
    [TestCase(TradeSettlementStatus.AwaitingReceipt)]
    [TestCase(TradeSettlementStatus.Completed)]
    [TestCase(TradeSettlementStatus.Disputed)]
    [TestCase(TradeSettlementStatus.SetAside)]
    public void PayAgain_FromAnyOtherStatus_ThrowsWithTheStatus(TradeSettlementStatus status)
    {
        var exception = Should.Throw<TradeSettlementStatusException>(() => InStatus(status).PayAgain());
        exception.Message.ShouldBe($"This settlement is {status} and cannot be paid again.");
    }

    [TestCase(TradeSettlementStatus.AwaitingPayment)]
    [TestCase(TradeSettlementStatus.AwaitingReceipt)]
    [TestCase(TradeSettlementStatus.PaymentFailed)]
    [TestCase(TradeSettlementStatus.Disputed)]
    public void SetAside_FromAnAllowedStatus_RecordsThePersonTheTimeAndTheNote(TradeSettlementStatus status)
    {
        var settlement = InStatus(status);

        settlement.SetAside(22, "  Agreed with the broker by phone  ", At.AddDays(1), paymentAttempted: false);

        (settlement.Status, settlement.SetAsideByUserId, settlement.SetAsideAt, settlement.SetAsideNote).ShouldBe((
            TradeSettlementStatus.SetAside, (int?)22, (DateTimeOffset?)At.AddDays(1), "Agreed with the broker by phone"));
        settlement.IsOpen.ShouldBeFalse();
    }

    [Test]
    public void SetAside_FromPaymentFailed_ClearsTheFailure()
    {
        var settlement = InStatus(TradeSettlementStatus.PaymentFailed);

        settlement.SetAside(22, "Paid by hand", At, paymentAttempted: false);

        (settlement.Status, settlement.Failure, settlement.FailureDetail)
            .ShouldBe((TradeSettlementStatus.SetAside, (TradeSettlementFailure?)null, (string?)null));
    }

    [Test]
    public void SetAside_AwaitingPayment_AfterASendAttempt_Throws()
    {
        var exception = Should.Throw<TradeSettlementStatusException>(() =>
            InStatus(TradeSettlementStatus.AwaitingPayment).SetAside(22, "Note", At, paymentAttempted: true));
        exception.Message.ShouldBe("The bank has been asked to pay this settlement, so it cannot be set aside.");
    }

    [Test]
    public void SetAside_Twice_OrFromCompleted_Throws_AndANoteIsRequired()
    {
        var settlement = InStatus(TradeSettlementStatus.Disputed);
        settlement.SetAside(22, "Note", At, paymentAttempted: false);
        Should.Throw<TradeSettlementStatusException>(() => settlement.SetAside(22, "Again", At, paymentAttempted: false));
        Should.Throw<TradeSettlementStatusException>(() =>
            InStatus(TradeSettlementStatus.Completed).SetAside(22, "Note", At, paymentAttempted: false));
        Should.Throw<DomainException>(() =>
            InStatus(TradeSettlementStatus.Disputed).SetAside(22, "   ", At, paymentAttempted: false));
        Should.Throw<DomainException>(() =>
            InStatus(TradeSettlementStatus.Disputed).SetAside(22, new string('n', 201), At, paymentAttempted: false));
    }

    private static TradeSettlement InStatus(TradeSettlementStatus status)
    {
        switch (status)
        {
            case TradeSettlementStatus.AwaitingPayment:
                return Create(Buys, 2, -1001.50m);
            case TradeSettlementStatus.AwaitingReceipt:
                return Create([new(OrderSide.Sell, 10m, 234m)], 1, 2340m);
            case TradeSettlementStatus.Completed:
                return Create([new(OrderSide.Buy, 1m, 1m), new(OrderSide.Sell, 1m, 1m)], 2, 0m);
            case TradeSettlementStatus.PaymentFailed:
                var failed = Create(Buys, 2, -1001.50m);
                failed.FailPayment(TradeSettlementFailure.PaymentRejected, "Refused");
                return failed;
            case TradeSettlementStatus.Disputed:
                return Create(Buys, 3, -1001.50m);
            case TradeSettlementStatus.SetAside:
                var setAside = Create(Buys, 3, -1001.50m);
                setAside.SetAside(22, "Note", At, paymentAttempted: false);
                return setAside;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static TradeSettlement Create(
        IReadOnlyCollection<SettlementFill> fills,
        int brokerFillCount,
        decimal brokerNet,
        string brokerAccount = "SIM-DEMO-USD",
        string brokerCurrency = "USD",
        bool configured = true,
        string settlementId = "0f8fad5b-S3") =>
        TradeSettlement.Create(
            ClientMoney(), "simulator", settlementId, Day, 90L, fills, brokerFillCount, brokerNet, brokerAccount,
            brokerCurrency, configured, At);

    private static ClientMoneyAccount ClientMoney()
    {
        var account = ClientMoneyAccount.Create(
            7, Currency.Create("USD", "USD", 2), "Simulator Bank", "Trust", "SIM-DEMO-USD", "simulator");
        account.Id = 40;
        return account;
    }
}
