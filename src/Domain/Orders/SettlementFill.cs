using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Domain.Orders;

/// <summary>One fill of a trade settlement as the platform counts it (trade-settlement R9).</summary>
public readonly record struct SettlementFill(OrderSide Side, decimal Quantity, decimal Price)
{
    /// <summary>R9: <c>Round(quantity × price, 4)</c>, plus for a sell and minus for a buy, seen from the trust
    /// account.</summary>
    public decimal Cash => throw new NotImplementedException();
}
