using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Infrastructure.Valuations;

/// <summary>A daily valuation job in which the run of at least one Tenant, or at least one account, failed
/// (valuation-run R14).</summary>
public sealed class ValuationRunFailedException(IReadOnlyList<Guid> tenants)
    : Exception($"The valuation run failed for tenants {string.Join(", ", tenants)}.");

/// <summary>One execution of <c>valuation-run</c> (valuation-run R14).</summary>
public sealed class ValuationRunJob(
    IApplicationDbContext db,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<ValuationRunJob> logger)
{
    public const string JobId = "valuation-run";

    public const string Queue = "valuation";

    public Task Run(CancellationToken cancellationToken)
    {
        _ = (db, scopes, time, logger);
        throw new NotImplementedException();
    }
}

/// <summary>R14: daily at 13:00 UTC on queue <c>valuation</c>, after <c>instrument-price-sync</c> at 12:00.</summary>
public static class ValuationRunRecurringJob
{
    public const string Cron = "0 13 * * *";

    public static void Register(IRecurringJobManager recurring) => throw new NotImplementedException();
}
