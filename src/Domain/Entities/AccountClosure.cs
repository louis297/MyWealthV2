namespace MyWealthV2.Domain.Entities;

/// <summary>
/// An account closure of a Custodial account (docs/features/account-closure.md §4, §5; roadmap B8).
/// </summary>
public class AccountClosure : BaseAuditableEntity
{
    public const int MemoMaxLength = 200;

    private AccountClosure()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public AccountClosureStatus Status { get; private set; }

    public AccountClosureCancelReason? CancelReason { get; private set; }

    public string Memo { get; private set; } = string.Empty;

    public int RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public decimal CashBalanceAtRequest { get; private set; }

    public decimal PayoutAmount { get; private set; }

    public int? NominatedBankAccountId { get; private set; }

    public int? WithdrawalId { get; private set; }

    public decimal? WriteOffAmount { get; private set; }

    public int? WriteOffTransactionId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public bool IsInProgress => throw new NotImplementedException();

    public static AccountClosure StartWithPayout(
        int tenantId,
        int accountId,
        string currency,
        int requestedByUserId,
        DateTimeOffset requestedAt,
        string memo,
        decimal cashBalance,
        decimal payout,
        int nominatedBankAccountId,
        int withdrawalId,
        Guid? publicId = null) => throw new NotImplementedException();

    public static AccountClosure CompleteAtOnce(
        int tenantId,
        int accountId,
        string currency,
        int requestedByUserId,
        DateTimeOffset requestedAt,
        string memo,
        decimal cashBalance,
        decimal? writeOffAmount,
        int? writeOffTransactionId,
        Guid? publicId = null) => throw new NotImplementedException();

    public void Complete(decimal? writeOffAmount, int? writeOffTransactionId, DateTimeOffset at) =>
        throw new NotImplementedException();

    public void Cancel(AccountClosureCancelReason reason, DateTimeOffset at) => throw new NotImplementedException();
}
