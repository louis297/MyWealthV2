using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Custodian;

/// <summary>What one send step did: the instructions it sent and how many calls failed (in-kind-transfers R9).</summary>
public sealed record CustodianSendOutcome(int Sent, int Failed);

/// <summary>
/// The send step of <c>custodian-sync:{connection}</c> (docs/features/in-kind-transfers.md R34, R35; ADR 0021 rules
/// 12, 13): every <c>Approved</c> in-kind transfer whose safekeeping account is active and on the connection, oldest
/// first. Each attempt is recorded and committed before the call; each outcome is one database transaction guarded by
/// the transfer's row version. One transfer's failure never stops the others.
/// </summary>
public sealed class SendInKindTransfers(
    IApplicationDbContext db,
    ICustodianTransfers transfers,
    ICustodianConnections connections,
    TimeProvider time,
    ILogger<SendInKindTransfers> logger)
{
    public Task<CustodianSendOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        _ = (db, transfers, connections, time, logger);
        throw new NotImplementedException();
    }
}
