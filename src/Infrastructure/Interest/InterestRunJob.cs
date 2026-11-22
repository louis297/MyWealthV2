using Hangfire;

namespace MyWealthV2.Infrastructure.Interest;

/// <summary>A daily interest job in which the run of at least one Tenant, or at least one account, failed (interest-run R31).</summary>
public sealed class InterestRunFailedException(IReadOnlyList<Guid> tenants)
    : Exception($"The interest run failed for tenants {string.Join(", ", tenants)}.");

/// <summary>
/// One execution of <c>interest-run</c> (interest-run R31): a run with trigger <c>Scheduled</c> for yesterday (UTC) for
/// every Tenant that has at least one interest rate schedule, in <c>Id</c> order, a disabled Tenant included, each in
/// its own scope. A failed Tenant is logged and does not stop the others; the job then fails once every Tenant was
/// tried. No automatic retries: the next day's run takes up what is left (R19, R22).
/// </summary>
public sealed class InterestRunJob
{
    public const string JobId = "interest-run";

    public const string Queue = "interest";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task Run(CancellationToken cancellationToken) => throw new NotImplementedException();
}

/// <summary>R31: daily at 00:30 UTC on queue <c>interest</c>, just after the UTC day ends.</summary>
public static class InterestRunRecurringJob
{
    public const string Cron = "30 0 * * *";

    public static void Register(IRecurringJobManager recurring)
    {
        throw new NotImplementedException();
    }
}
