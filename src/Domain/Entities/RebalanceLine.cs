using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Domain.Entities;

/// <summary>One line of a rebalance proposal (docs/features/rebalances.md R9): an Instrument held or targeted, the
/// quantity held, its weight now and its target weight, and the trade the plan would make at the stored price.
/// Never changed after the proposal.</summary>
public class RebalanceLine : BaseEntity
{
    private RebalanceLine()
    {
    }

    public int TenantId { get; private set; }

    public int RebalanceId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>The holding's market value over <c>V</c>, in percent with 2 decimals.</summary>
    public decimal Weight { get; private set; }

    /// <summary>Null outside the targets.</summary>
    public decimal? TargetWeight { get; private set; }

    /// <summary>The Instrument's quote currency.</summary>
    public string Currency { get; private set; } = string.Empty;

    public OrderSide? Side { get; private set; }

    public decimal? TradeQuantity { get; private set; }

    public decimal? ReferencePrice { get; private set; }

    public DateOnly? ReferencePriceDate { get; private set; }

    public decimal? EstimatedGross { get; private set; }

    public decimal? EstimatedFee { get; private set; }

    internal static RebalanceLine Create(int tenantId, RebalanceLineSpec spec)
    {
        if (spec.Quantity < 0m)
        {
            throw new DomainException("A line's quantity is not below 0.");
        }

        var currency = spec.Currency?.Trim() ?? string.Empty;
        if (currency.Length != 3)
        {
            throw new DomainException("A line has a 3-letter currency.");
        }

        var line = new RebalanceLine
        {
            TenantId = tenantId,
            InstrumentId = spec.InstrumentId,
            Quantity = spec.Quantity,
            Weight = Math.Round(spec.Weight, 2, MidpointRounding.AwayFromZero),
            TargetWeight = spec.TargetWeight,
            Currency = currency.ToUpperInvariant()
        };

        if (spec.Trade is { } trade)
        {
            if (trade.Quantity <= 0m || trade.ReferencePrice <= 0m || trade.EstimatedGross < 0m || trade.EstimatedFee < 0m)
            {
                throw new DomainException("A proposed trade has a quantity and a price above 0.");
            }

            line.Side = trade.Side;
            line.TradeQuantity = trade.Quantity;
            line.ReferencePrice = trade.ReferencePrice;
            line.ReferencePriceDate = trade.ReferencePriceDate;
            line.EstimatedGross = trade.EstimatedGross;
            line.EstimatedFee = trade.EstimatedFee;
        }

        return line;
    }
}
