namespace MyWealthV2.Domain.Exceptions;

/// <summary>The schedule row may already apply to an accrued day, so it cannot change (interest-run R13, R14).</summary>
public class InterestScheduleInEffectException : DomainException
{
    public InterestScheduleInEffectException()
        : base("The interest rate schedule is in effect and cannot be changed.")
    {
    }
}
