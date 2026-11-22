namespace MyWealthV2.Domain.Custody;

/// <summary>
/// Orders R40: the available quantity of a holding is its quantity minus the quantity reserved for the Account's live
/// sell orders in that Instrument. On a Custodial account a sell order and an adjustment that lowers the holding keep
/// it at or above zero.
/// </summary>
public static class AvailableQuantity
{
    public static decimal Of(decimal quantity, decimal reserved) => throw new NotImplementedException();

    /// <summary>True when a holding of <paramref name="quantityAfter"/> still covers the reserved quantity.</summary>
    public static bool Allows(decimal quantityAfter, decimal reserved) => throw new NotImplementedException();
}
