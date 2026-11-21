namespace MyWealthV2.Domain.Enums;

/// <summary>Where a nominated bank account is in its check (withdrawals R7).</summary>
public enum NominatedBankAccountStatus
{
    Pending = 0,
    Verified = 1,
    Rejected = 2,
    Removed = 3
}
