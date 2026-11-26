using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>Valuation-run R13: one run for one Tenant through a date; used by the daily job and the manual run.</summary>
public interface IRunValuations
{
    Task<ValuationRun> RunAsync(
        int tenantId,
        DateOnly throughDate,
        ValuationRunTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken);
}
