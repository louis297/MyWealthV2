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
    public bool IsOpen => Status is TradeSettlementStatus.AwaitingPayment or TradeSettlementStatus.AwaitingReceipt
        or TradeSettlementStatus.PaymentFailed or TradeSettlementStatus.Disputed;

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
        DateTimeOffset at)
    {
        if (fills.Count == 0)
        {
            throw new DomainException("A trade settlement has at least one fill.");
        }

        if (string.IsNullOrWhiteSpace(brokerSettlementId) || brokerSettlementId.Length > BrokerSettlementIdMaxLength)
        {
            throw new DomainException($"Broker settlement id must be 1 to {BrokerSettlementIdMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(brokerSettlementAccount) || brokerSettlementAccount.Length > SettlementAccountMaxLength)
        {
            throw new DomainException($"Settlement account must be 1 to {SettlementAccountMaxLength} characters.");
        }

        var currency = clientMoney.Currency.Trim();
        var settlement = new TradeSettlement
        {
            PublicId = Guid.NewGuid(),
            TenantId = clientMoney.TenantId,
            ClientMoneyAccountId = clientMoney.Id,
            Connection = connection,
            BrokerSettlementId = brokerSettlementId,
            Currency = currency,
            SettledDate = settledDate,
            ExecutionReportId = executionReportId,
            FillCount = fills.Count,
            NetAmount = fills.Sum(fill => fill.Cash),
            BrokerFillCount = brokerFillCount,
            BrokerNetAmount = brokerNetAmount,
            BrokerSettlementAccount = brokerSettlementAccount
        };

        var agreed = settlement.FillCount == brokerFillCount
            && settlement.NetAmount == brokerNetAmount
            && NominatedBankAccount.KeyOf(brokerSettlementAccount) == NominatedBankAccount.KeyOf(clientMoney.AccountNumber)
            && string.Equals(brokerCurrency.Trim(), currency, StringComparison.OrdinalIgnoreCase);
        if (!agreed)
        {
            settlement.Status = TradeSettlementStatus.Disputed;
        }
        else if (settlement.NetAmount == 0m)
        {
            settlement.Status = TradeSettlementStatus.Completed;
            settlement.CompletedAt = at;
        }
        else if (settlement.NetAmount > 0m)
        {
            settlement.Status = TradeSettlementStatus.AwaitingReceipt;
        }
        else if (brokerAccountConfigured)
        {
            settlement.Status = TradeSettlementStatus.AwaitingPayment;
        }
        else
        {
            settlement.Status = TradeSettlementStatus.PaymentFailed;
            settlement.Failure = TradeSettlementFailure.NoBrokerAccount;
        }

        return settlement;
    }

    /// <summary>R18, R19: the statement line showed the cash.</summary>
    public void Complete(DateTimeOffset at)
    {
        if (Status is not (TradeSettlementStatus.AwaitingPayment or TradeSettlementStatus.AwaitingReceipt))
        {
            throw new TradeSettlementStatusException($"This settlement is {Status} and cannot be completed.");
        }

        Status = TradeSettlementStatus.Completed;
        CompletedAt = at;
    }

    /// <summary>R17, R20: the bank refused or returned the payment. A returned payment fails a completed
    /// settlement.</summary>
    public void FailPayment(TradeSettlementFailure failure, string? detail)
    {
        var from = Status == TradeSettlementStatus.AwaitingPayment
            || (Status == TradeSettlementStatus.Completed && failure == TradeSettlementFailure.PaymentReturned && NetAmount < 0m);
        if (!from)
        {
            throw new TradeSettlementStatusException($"This settlement is {Status} and its payment cannot fail.");
        }

        if (detail is { Length: > FailureDetailMaxLength })
        {
            throw new DomainException($"Failure detail must be at most {FailureDetailMaxLength} characters.");
        }

        Status = TradeSettlementStatus.PaymentFailed;
        Failure = failure;
        FailureDetail = detail;
        CompletedAt = null;
    }

    /// <summary>R14: a new payment is made after a failure.</summary>
    public void PayAgain()
    {
        if (Status != TradeSettlementStatus.PaymentFailed)
        {
            throw new TradeSettlementStatusException($"This settlement is {Status} and cannot be paid again.");
        }

        Status = TradeSettlementStatus.AwaitingPayment;
        Failure = null;
        FailureDetail = null;
    }

    /// <summary>R15: a TenantAdmin ends it by hand. From <c>AwaitingPayment</c> only while its payment has no send
    /// attempt.</summary>
    public void SetAside(int byUserId, string note, DateTimeOffset at, bool paymentAttempted)
    {
        if (!IsOpen)
        {
            throw new TradeSettlementStatusException($"This settlement is {Status} and cannot be set aside.");
        }

        if (Status == TradeSettlementStatus.AwaitingPayment && paymentAttempted)
        {
            throw new TradeSettlementStatusException(
                "The bank has been asked to pay this settlement, so it cannot be set aside.");
        }

        var trimmed = note?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NoteMaxLength)
        {
            throw new DomainException($"Note must be 1 to {NoteMaxLength} characters.");
        }

        Status = TradeSettlementStatus.SetAside;
        SetAsideByUserId = byUserId;
        SetAsideAt = at;
        SetAsideNote = trimmed;
    }
}
