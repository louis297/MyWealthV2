namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The fee behind one <see cref="CashLegRole.Fee"/> cash leg (trade-fees R18): kind, schedule, basis and fee in the
/// quote currency. Exactly one per <c>Fee</c> leg; immutable after create.
/// </summary>
public class TransactionFeeDetail
{
    private TransactionFeeDetail()
    {
    }

    public int CashLegId { get; private set; }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public FeeKind Kind { get; private set; }

    public int? TradeFeeScheduleId { get; private set; }

    public decimal BasisAmount { get; private set; }

    public decimal FeeAmount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    internal static TransactionFeeDetail Create(
        int tenantId,
        FeeKind kind,
        int? scheduleId,
        decimal basisAmount,
        decimal feeAmount,
        string currency) =>
        new()
        {
            TenantId = tenantId,
            Kind = kind,
            TradeFeeScheduleId = scheduleId,
            BasisAmount = basisAmount,
            FeeAmount = feeAmount,
            Currency = currency
        };
}
