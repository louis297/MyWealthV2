namespace MyWealthV2.Domain.Exceptions;

/// <summary>The schedule row may already apply to a posted trade, so it cannot change (trade-fees R11, R12).</summary>
public class TradeFeeScheduleInEffectException : DomainException
{
    public TradeFeeScheduleInEffectException()
        : base("The trade fee schedule is in effect and cannot be changed.")
    {
    }
}
