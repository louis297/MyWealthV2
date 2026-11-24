namespace MyWealthV2.Domain.Custody;

/// <summary>
/// In-kind-transfers R44, R26: the part of a holding a transfer out may take: not reserved for a sell order or another
/// transfer out, and not from a purchase that has not settled.
/// </summary>
public static class TransferableQuantity
{
    /// <summary><c>Max(0, quantity − reserved − unsettled)</c>.</summary>
    public static decimal Of(decimal quantity, decimal reserved, decimal unsettled) =>
        Math.Max(0m, quantity - reserved - unsettled);

    /// <summary>True when a transfer out of <paramref name="quantity"/> fits in <paramref name="transferable"/>.</summary>
    public static bool Allows(decimal quantity, decimal transferable) => quantity > 0 && quantity <= transferable;
}
