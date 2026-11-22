using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Interest.Runs;

/// <summary>Interest-run R20, R30: one run for one Tenant and one as-of date. Used by the daily job and the manual trigger.</summary>
public interface IRunInterest
{
    Task<InterestRun> RunAsync(
        int tenantId,
        DateOnly asOfDate,
        InterestRunTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken);
}
