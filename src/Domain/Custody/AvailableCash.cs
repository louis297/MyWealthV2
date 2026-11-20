namespace MyWealthV2.Domain.Custody;

/// <summary>
/// Withdrawals R35–R37: available cash is the cash balance minus the cash reserved for open withdrawals. Every write
/// that lowers cash on a Custodial account keeps it at or above zero, except the payout.
/// </summary>
public static class AvailableCash
{
    public static decimal Of(decimal cashSum, decimal reserved) => cashSum - reserved;

    /// <summary>True when a cash <c>SUM</c> of <paramref name="cashSumAfter"/> leaves available cash at or above zero.</summary>
    public static bool Allows(decimal cashSumAfter, decimal reserved) => Of(cashSumAfter, reserved) >= 0m;
}
