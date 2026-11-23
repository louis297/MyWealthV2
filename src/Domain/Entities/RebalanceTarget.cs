using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Domain.Entities;

/// <summary>One line of a rebalance's own targets: the copy of the portfolio's line it was proposed with
/// (docs/features/rebalances.md R9, R10).</summary>
public class RebalanceTarget : BaseEntity
{
    private RebalanceTarget()
    {
    }

    public int TenantId { get; private set; }

    public int RebalanceId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal TargetWeight { get; private set; }

    public int SortOrder { get; private set; }

    internal static RebalanceTarget Create(int tenantId, RebalanceTargetSpec spec, int sortOrder)
    {
        if (spec.TargetWeight is <= 0m or > 100m)
        {
            throw new DomainException("A target weight is above 0 and at most 100.");
        }

        return new RebalanceTarget
        {
            TenantId = tenantId,
            InstrumentId = spec.InstrumentId,
            TargetWeight = spec.TargetWeight,
            SortOrder = sortOrder
        };
    }
}
