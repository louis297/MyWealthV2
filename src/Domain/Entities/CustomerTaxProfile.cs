using MyWealthV2.Domain.Interest;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A Customer's tax profile (interest-run §4.2): tax residency, whether the firm holds the IRD number, and the RWT
/// rate the Customer chose. One row per Customer, keyed by <see cref="UserId"/>; a Customer with no row reads as
/// <see cref="NotProvided"/>. The IRD number itself is never stored. No history and no event (R7).
/// </summary>
public class CustomerTaxProfile : BaseAuditableEntity
{
    private CustomerTaxProfile()
    {
    }

    public int TenantId { get; private set; }

    /// <summary>The Customer.</summary>
    public int UserId { get; private set; }

    public TaxResidency TaxResidency { get; private set; }

    public bool IrdNumberHeld { get; private set; }

    public decimal? RwtRatePercent { get; private set; }

    public int UpdatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>The profile of a Customer with no row (R2): <c>NewZealand</c>, IRD number not held, no rate.</summary>
    public static CustomerTaxProfile NotProvided => new();

    /// <summary>R4 for the stored attributes.</summary>
    public Withholding Withholding => WithholdingRates.For(this);

    /// <summary>The first write (R6).</summary>
    public static CustomerTaxProfile Create(
        int tenantId,
        int userId,
        TaxResidency residency,
        bool irdNumberHeld,
        decimal? rwtRatePercent,
        int updatedByUserId,
        DateTimeOffset updatedAt) =>
        throw new NotImplementedException();

    /// <summary>R3, R6: replaces the three attributes.</summary>
    public void Set(
        TaxResidency residency,
        bool irdNumberHeld,
        decimal? rwtRatePercent,
        int updatedByUserId,
        DateTimeOffset updatedAt) =>
        throw new NotImplementedException();
}
