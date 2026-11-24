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

    /// <summary>Dividends R13: the holding's quantity when the dividend went ex; <c>null</c> on another kind.</summary>
    public decimal? EntitledQuantity { get; private set; }

    /// <summary>Dividends R14: the source amounts in the quote currency, fixed when the dividend went ex.</summary>
    public decimal? GrossAmount { get; private set; }

    public decimal? ImputationCredit { get; private set; }

    public decimal? ForeignTax { get; private set; }

    /// <summary>Dividends R18: the entitlement is reinvested under the dividend's plan.</summary>
    public bool Reinvests { get; private set; }

    /// <summary>Dividends R19: the part of the entitled quantity that takes part in the plan.</summary>
    public decimal? ParticipatingQuantity { get; private set; }

    /// <summary>Dividends R30: the new shares this account gets.</summary>
    public decimal? ReinvestedQuantity { get; private set; }

    /// <summary>Dividends R36: the net cash of the posting, in the Account's currency.</summary>
    public decimal? NetAmount { get; private set; }

    public string? NetCurrency { get; private set; }

    public bool IsDividend => throw new NotImplementedException();

    public int? TransactionId { get; private set; }

    /// <summary>When it left <c>Pending</c>.</summary>
    public DateTimeOffset? PostedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static CorporateActionEntitlement Create(int tenantId, int corporateActionId, int accountId) =>
        new()
        {
            TenantId = tenantId,
            CorporateActionId = corporateActionId,
            AccountId = accountId,
            Status = EntitlementStatus.Pending
        };

    /// <summary>Dividends R13, R14, R18, R19: the entitlement of a dividend that went ex, with its entitled quantity and
    /// source amounts. A gross of 0 is <c>NotApplied</c> with <c>NothingToPay</c>.</summary>
    public static CorporateActionEntitlement CreateDividend(
        int tenantId,
        int corporateActionId,
        int accountId,
        decimal entitledQuantity,
        CorporateActions.DividendSourceAmounts amounts,
        bool reinvests,
        decimal? participatingQuantity,
        DateTimeOffset at) =>
        throw new NotImplementedException();

    /// <summary>R25: posted as <paramref name="transactionId"/>, from <paramref name="quantityBefore"/> to
    /// <paramref name="quantityAfter"/>.</summary>
    public void MarkPosted(decimal quantityBefore, decimal quantityAfter, int transactionId, DateTimeOffset at)
    {
        EnsurePending();
        if (quantityBefore <= 0m || quantityAfter <= 0m || quantityAfter == quantityBefore)
        {
            throw new DomainException("A posted entitlement changes a holding above 0 to another quantity above 0.");
        }

        if (transactionId <= 0)
        {
            throw new DomainException("A posted entitlement needs its Transaction.");
        }

        Status = EntitlementStatus.Posted;
        QuantityBefore = quantityBefore;
        QuantityAfter = quantityAfter;
        TransactionId = transactionId;
        PostedAt = at.ToUniversalTime();
    }

    /// <summary>R25: nothing was posted, for <paramref name="reason"/>.</summary>
    public void MarkNotApplied(EntitlementReason reason, DateTimeOffset at)
    {
        EnsurePending();
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Unknown entitlement reason.");
        }

        Status = EntitlementStatus.NotApplied;
        Reason = reason;
        PostedAt = at.ToUniversalTime();
    }

    private void EnsurePending()
    {
        if (Status != EntitlementStatus.Pending)
        {
            throw new DomainException($"This entitlement is {Status} and cannot change.");
        }
    }
}
