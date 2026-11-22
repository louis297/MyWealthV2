namespace MyWealthV2.Domain.Entities;

/// <summary>
/// What one Custodial account gets from one corporate action (corporate-actions R22, R25): stored <c>Pending</c> when
/// the action takes effect, then <c>Posted</c> once with both quantities and its Transaction, or <c>NotApplied</c>
/// with a reason. It leaves <c>Pending</c> once and only once.
/// </summary>
public class CorporateActionEntitlement
{
    private CorporateActionEntitlement()
    {
    }

    public long Id { get; private set; }

    public int TenantId { get; private set; }

    public int CorporateActionId { get; private set; }

    public int AccountId { get; private set; }

    public EntitlementStatus Status { get; private set; }

    public EntitlementReason? Reason { get; private set; }

    public decimal? QuantityBefore { get; private set; }

    public decimal? QuantityAfter { get; private set; }

    public int? TransactionId { get; private set; }

    /// <summary>When it left <c>Pending</c>.</summary>
    public DateTimeOffset? PostedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static CorporateActionEntitlement Create(int tenantId, int corporateActionId, int accountId) => throw new NotImplementedException();

    /// <summary>R25: posted as <paramref name="transactionId"/>, from <paramref name="quantityBefore"/> to
    /// <paramref name="quantityAfter"/>.</summary>
    public void MarkPosted(decimal quantityBefore, decimal quantityAfter, int transactionId, DateTimeOffset at)
    {
        throw new NotImplementedException();
    }

    /// <summary>R25: nothing was posted, for <paramref name="reason"/>.</summary>
    public void MarkNotApplied(EntitlementReason reason, DateTimeOffset at)
    {
        throw new NotImplementedException();
    }

    private void EnsurePending()
    {
        if (Status != EntitlementStatus.Pending)
        {
            throw new DomainException($"This entitlement is {Status} and cannot change.");
        }
    }
}
