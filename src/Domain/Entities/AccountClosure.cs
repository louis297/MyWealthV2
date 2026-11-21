namespace MyWealthV2.Domain.Entities;

/// <summary>
/// An account closure of a Custodial account (docs/features/account-closure.md §4, §5; roadmap B8): who closed the
/// account, when and why, the cash balance at request, what was paid out (as a withdrawal) and what was written off.
/// With a payout it is <c>InProgress</c> until the payout is posted (R8, R17) or its withdrawal ends (R20); without one
/// it is <c>Completed</c> when it is created (R9). It raises no domain event of its own (R10).
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

    /// <summary>R11: the Account is closing while its closure is in progress.</summary>
    public bool IsInProgress => Status == AccountClosureStatus.InProgress;

    /// <summary>R8: a closure with a payout of <paramref name="payout"/>, paid by the withdrawal
    /// <paramref name="withdrawalId"/> to <paramref name="nominatedBankAccountId"/>. <c>InProgress</c>.</summary>
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
        Guid? publicId = null)
    {
        if (payout <= 0m)
        {
            throw new DomainException("A payout must be greater than zero.");
        }

        if (payout > cashBalance)
        {
            throw new DomainException("The payout cannot be more than the cash balance.");
        }

        var closure = Create(tenantId, accountId, currency, requestedByUserId, requestedAt, memo, cashBalance, publicId);
        closure.Status = AccountClosureStatus.InProgress;
        closure.PayoutAmount = payout;
        closure.NominatedBankAccountId = nominatedBankAccountId;
        closure.WithdrawalId = withdrawalId;
        return closure;
    }

    /// <summary>R9: a closure with nothing to pay out (the cash balance is a remainder), <c>Completed</c> at
    /// <paramref name="requestedAt"/>, with the write-off when there was one.</summary>
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
        Guid? publicId = null)
    {
        if (cashBalance < 0m || cashBalance != (writeOffAmount ?? 0m))
        {
            throw new DomainException("A closure without a payout writes off the whole cash balance.");
        }

        var closure = Create(tenantId, accountId, currency, requestedByUserId, requestedAt, memo, cashBalance, publicId);
        closure.SetWriteOff(writeOffAmount, writeOffTransactionId);
        closure.Status = AccountClosureStatus.Completed;
        closure.CompletedAt = requestedAt;
        return closure;
    }

    /// <summary>R17: the payout is posted, the remainder written off (when there was one) and the Account closed.</summary>
    public void Complete(decimal? writeOffAmount, int? writeOffTransactionId, DateTimeOffset at)
    {
        EnsureInProgress("completed");
        SetWriteOff(writeOffAmount, writeOffTransactionId);
        Status = AccountClosureStatus.Completed;
        CompletedAt = at;
    }

    /// <summary>R19, R20: the closure ends without closing the Account.</summary>
    public void Cancel(AccountClosureCancelReason reason, DateTimeOffset at)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("A cancelled closure needs a reason.");
        }

        EnsureInProgress("cancelled");
        Status = AccountClosureStatus.Cancelled;
        CancelReason = reason;
        EndedAt = at;
    }

    private static AccountClosure Create(
        int tenantId,
        int accountId,
        string currency,
        int requestedByUserId,
        DateTimeOffset requestedAt,
        string memo,
        decimal cashBalance,
        Guid? publicId)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("Currency is required.");
        }

        if (cashBalance < 0m)
        {
            throw new DomainException("The cash balance of a closed account cannot be negative.");
        }

        return new AccountClosure
        {
            TenantId = tenantId,
            AccountId = accountId,
            Currency = currency.Trim().ToUpperInvariant(),
            RequestedByUserId = requestedByUserId,
            RequestedAt = requestedAt,
            Memo = RequiredMemo(memo),
            CashBalanceAtRequest = cashBalance,
            PublicId = publicId ?? Guid.NewGuid()
        };
    }

    private void SetWriteOff(decimal? amount, int? transactionId)
    {
        if (amount.HasValue != transactionId.HasValue)
        {
            throw new DomainException("A write-off has both its amount and its transaction.");
        }

        if (amount is <= 0m)
        {
            throw new DomainException("A write-off must be greater than zero.");
        }

        WriteOffAmount = amount;
        WriteOffTransactionId = transactionId;
    }

    private void EnsureInProgress(string action)
    {
        if (Status != AccountClosureStatus.InProgress)
        {
            throw new DomainException($"This account closure is {Status} and cannot be {action}.");
        }
    }

    /// <summary>R2: why the account is closed, trimmed, 1 to 200 characters.</summary>
    private static string RequiredMemo(string? memo)
    {
        var trimmed = memo?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException("Memo is required.");
        }

        if (trimmed.Length > MemoMaxLength)
        {
            throw new DomainException($"Memo must be at most {MemoMaxLength} characters.");
        }

        return trimmed;
    }
}
