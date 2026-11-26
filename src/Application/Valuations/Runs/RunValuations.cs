using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>
/// Valuation-run R2, R13: stores the run, then values every Custodial account of the Tenant, any status, in
/// <c>Id</c> order through the through date. A failure on one account is logged and does not stop the others.
/// Reads ignore the tenant filter and name the Tenant themselves.
/// </summary>
public class RunValuations(
    IApplicationDbContext db,
    IStoreAccountValuations store,
    TimeProvider time,
    ILogger<RunValuations> logger) : IRunValuations
{
    public Task<ValuationRun> RunAsync(
        int tenantId,
        DateOnly throughDate,
        ValuationRunTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken)
    {
        _ = (db, store, time, logger);
        throw new NotImplementedException();
    }
}
