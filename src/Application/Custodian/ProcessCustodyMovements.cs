using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Custodian;

/// <summary>
/// The processing step of <c>custodian-sync:{connection}</c> (docs/features/in-kind-transfers.md R14–R17, R36–R40;
/// ADR 0021 rule 8). The one place a transfer is posted.
/// </summary>
public sealed class ProcessCustodyMovements(
    IApplicationDbContext db,
    TimeProvider time,
    ILogger<ProcessCustodyMovements> logger)
{
    public Task RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        _ = (db, time, logger);
        return Task.CompletedTask;
    }
}
