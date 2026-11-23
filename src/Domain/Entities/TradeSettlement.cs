using MyWealthV2.Domain.Orders;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The platform's record of one broker settlement for one client money account (docs/features/trade-settlement.md
/// R10): its fills, the platform's own figures and the broker's, and a status. It writes no Transaction.
/// </summary>
public class TradeSettlement : BaseAuditableEntity
{
    public const int BrokerSettlementIdMaxLength = 100;
    public const int SettlementAccountMaxLength = 34;
    public const int FailureDetailMaxLength = 140;
    public const int NoteMaxLength = 200;

    private TradeSettlement()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int ClientMoneyAccountId { get; private set; }

    public string Connection { get; private set; } = string.Empty;

    public string BrokerSettlementId { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    public DateOnly SettledDate { get; private set; }

    /// <summary>The platform's count of the settlement's fills (R9).</summary>
    public int FillCount { get; private set; }

    /// <summary>The platform's net, seen from the trust account: above 0 the broker pays the firm (R9).</summary>
    public decimal NetAmount { get; private set; }

    public int BrokerFillCount { get; private set; }

    public decimal BrokerNetAmount { get; private set; }

    public string BrokerSettlementAccount { get; private set; } = string.Empty;

    public long ExecutionReportId { get; private set; }

    public TradeSettlementStatus Status { get; private set; }

    public TradeSettlementFailure? Failure { get; private set; }

    public string? FailureDetail { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int? SetAsideByUserId { get; private set; }

    public DateTimeOffset? SetAsideAt { get; private set; }

    public string? SetAsideNote { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>R10: open while the platform still waits for the bank or a person.</summary>
    public bool IsOpen => throw new NotImplementedException();

    /// <summary>
    /// R8, R9, R11–R13: stores one broker settlement for <paramref name="clientMoney"/>. The platform's figures come
    /// from <paramref name="fills"/>; it is agreed when the broker's count, net, settlement account and currency are
    /// the same, and then <c>Completed</c> (net 0), <c>AwaitingReceipt</c> (net above 0), <c>AwaitingPayment</c> (net
    /// below 0 with the broker's bank account configured) or <c>PaymentFailed</c> with <c>NoBrokerAccount</c>.
    /// Otherwise it is <c>Disputed</c>.
    /// </summary>
    public static TradeSettlement Create(
        ClientMoneyAccount clientMoney,
        string connection,
        string brokerSettlementId,
        DateOnly settledDate,
        long executionReportId,
        IReadOnlyCollection<SettlementFill> fills,
        int brokerFillCount,
        decimal brokerNetAmount,
        string brokerSettlementAccount,
        string brokerCurrency,
        bool brokerAccountConfigured,
        DateTimeOffset at) =>
        throw new NotImplementedException();

    /// <summary>R18, R19: the statement line showed the cash.</summary>
    public void Complete(DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R17, R20: the bank refused or returned the payment. A returned payment fails a completed
    /// settlement.</summary>
    public void FailPayment(TradeSettlementFailure failure, string? detail) => throw new NotImplementedException();

    /// <summary>R14: a new payment is made after a failure.</summary>
    public void PayAgain() => throw new NotImplementedException();

    /// <summary>R15: a TenantAdmin ends it by hand. From <c>AwaitingPayment</c> only while its payment has no send
    /// attempt.</summary>
    public void SetAside(int byUserId, string note, DateTimeOffset at, bool paymentAttempted) =>
        throw new NotImplementedException();
}
