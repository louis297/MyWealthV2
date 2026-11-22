using MyWealthV2.Domain.CorporateActions;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The event behind an entitlement posting (corporate-actions R27): kind, ratio, ex-date, record date, payment date,
/// and the holding's quantity before and after. Exactly one on every Transaction with source <c>CorporateAction</c>
/// and on its Reversal, none elsewhere; immutable.
/// </summary>
public class TransactionCorporateActionDetail
{
    private TransactionCorporateActionDetail()
    {
    }

    public int TransactionId { get; private set; }

    public int TenantId { get; private set; }

    public CorporateActionKind Kind { get; private set; }

    public int RatioNew { get; private set; }

    public int RatioHeld { get; private set; }

    public DateOnly ExDate { get; private set; }

    public DateOnly? RecordDate { get; private set; }

    public DateOnly? PaymentDate { get; private set; }

    public decimal QuantityBefore { get; private set; }

    public decimal QuantityAfter { get; private set; }

    internal static TransactionCorporateActionDetail Create(
        int tenantId,
        CorporateActionKind kind,
        CorporateActionRatio ratio,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate,
        decimal quantityBefore,
        decimal quantityAfter) =>
        new()
        {
            TenantId = tenantId,
            Kind = kind,
            RatioNew = ratio.New,
            RatioHeld = ratio.Held,
            ExDate = exDate,
            RecordDate = recordDate,
            PaymentDate = paymentDate,
            QuantityBefore = quantityBefore,
            QuantityAfter = quantityAfter
        };

    /// <summary>A new row with the same values, for the Reversal (R27).</summary>
    internal TransactionCorporateActionDetail Copy() =>
        Create(TenantId, Kind, new CorporateActionRatio(RatioNew, RatioHeld), ExDate, RecordDate, PaymentDate, QuantityBefore, QuantityAfter);
}
