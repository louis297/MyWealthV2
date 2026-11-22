namespace MyWealthV2.Application.Interest.Runs;

/// <summary>Interest-run R32: a SystemAdmin starts a run for a Tenant and an as-of date; it runs at once.</summary>
public class StartInterestRunCommandHandler
{
    public const string FutureMessage = "Interest can be run only for days that have ended.";
}
