using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

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
    public async Task<CustodianSendOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Custodian connection {Connection} is not configured on this host; nothing is sent.", connection);
            return new CustodianSendOutcome(0, 0);
        }

        var ids = await (
                from transfer in db.InKindTransfers.IgnoreQueryFilters().AsNoTracking()
                join safekeeping in db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
                    on transfer.SafekeepingAccountId equals safekeeping.Id
                join tenant in db.Tenants.AsNoTracking() on transfer.TenantId equals tenant.Id
                where transfer.Status == InKindTransferStatus.Approved
                      && safekeeping.IsActive
                      && safekeeping.CustodianConnection == connection
                      // R34: a disabled Tenant's transfer gets no first attempt; an attempted one is sent again.
                      && (tenant.IsActive || transfer.Attempts > 0)
                orderby transfer.Id
                select transfer.Id)
            .ToListAsync(cancellationToken);

        var sent = 0;
        var failed = 0;
        foreach (var id in ids)
        {
            try
            {
                var result = await SendAsync(id, connection, cancellationToken);
                if (result is null)
                {
                    continue;
                }

                sent++;
                if (result.Failure is not null)
                {
                    failed++;
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // R32: a cancel won the race before the attempt was recorded; the transfer is no longer sent.
                logger.LogWarning("In-kind transfer {TransferId} changed while it was sent; it is skipped.", id);
            }
        }

        return new CustodianSendOutcome(sent, failed);
    }

    private async Task<CustodianTransferResult?> SendAsync(int id, string connection, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var transfer = await db.InKindTransfers.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
        if (transfer.Status != InKindTransferStatus.Approved)
        {
            return null;
        }

        var safekeeping = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == transfer.SafekeepingAccountId, cancellationToken);
        var symbol = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == transfer.InstrumentId)
            .Select(row => row.Symbol)
            .SingleAsync(cancellationToken);
        var request = new CustodianTransferRequest(
            connection, safekeeping.AccountNumber, transfer.PublicId, transfer.Direction, symbol, transfer.Currency.Trim(),
            transfer.Quantity,
            new CustodianCounterparty(transfer.CounterpartyName, transfer.CounterpartyAccount, transfer.CounterpartyHolderName));

        transfer.RecordAttempt(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var result = await transfers.SendAsync(request, cancellationToken);
        try
        {
            Apply(transfer, result);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // R35: a movement completed the transfer meanwhile; it keeps its status and only takes the custodian's id.
            db.ClearTrackedChanges();
            var current = await db.InKindTransfers.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
            if (result.TransferId is { } transferId && current.Status != InKindTransferStatus.Approved)
            {
                current.Accept(transferId, time.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (current.Status == InKindTransferStatus.Approved)
            {
                Apply(current, result);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return result;
    }

    private void Apply(InKindTransfer transfer, CustodianTransferResult result)
    {
        if (result.TransferId is { } transferId)
        {
            transfer.Accept(transferId, time.GetUtcNow());
        }
        else if (result.RejectReason is { } reason)
        {
            if (transfer.Status == InKindTransferStatus.Approved)
            {
                transfer.Fail(reason, time.GetUtcNow());
            }
        }
        else
        {
            transfer.RecordFailure(result.Failure ?? CustodianFailure.BadResponse);
        }
    }
}
