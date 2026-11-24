using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Custodian;

/// <summary>
/// The election step of <c>custodian-sync:{connection}</c> (docs/features/dividends.md R21–R23; ADR 0021 rules 12,
/// 13): every <c>Pending</c> reinvestment election whose safekeeping account is on the connection, oldest first, after
/// the in-kind transfers. An election whose dividend already has its receipt, or whose safekeeping account is no longer
/// active, lapses unsent. Each attempt is recorded and committed before the call; a failed call leaves the election
/// <c>Pending</c> for the next run, with the same body.
/// </summary>
public sealed class SendReinvestmentElections(
    IApplicationDbContext db,
    ICustodianElections elections,
    ICustodianConnections connections,
    TimeProvider time,
    ILogger<SendReinvestmentElections> logger)
{
    public async Task<CustodianSendOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Custodian connection {Connection} is not configured on this host; no election is sent.", connection);
            return new CustodianSendOutcome(0, 0);
        }

        var ids = await (
                from action in db.CorporateActions.IgnoreQueryFilters().AsNoTracking()
                join safekeeping in db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
                    on action.SafekeepingAccountId equals safekeeping.Id
                where action.ElectionStatus == ReinvestmentElectionStatus.Pending
                      && safekeeping.CustodianConnection == connection
                orderby action.EffectiveAt, action.Id
                select action.Id)
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
                logger.LogWarning("Dividend {CorporateActionId} changed while its election was sent; the next run sees it.", id);
            }
        }

        return new CustodianSendOutcome(sent, failed);
    }

    private async Task<CustodianElectionResult?> SendAsync(int id, string connection, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var action = await db.CorporateActions.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
        if (action.ElectionStatus != ReinvestmentElectionStatus.Pending)
        {
            return null;
        }

        var safekeeping = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == action.SafekeepingAccountId, cancellationToken);
        var now = time.GetUtcNow();

        // R22: too late to elect once the receipt is in, and nothing to elect through an inactive account.
        if (action.ReceiptLineId is not null || !safekeeping.IsActive)
        {
            action.LapseElection();
            await PayDividend.TryAsync(db, action, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var instrument = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == action.InstrumentId)
            .Select(row => new { row.Symbol, row.QuoteCurrency })
            .SingleAsync(cancellationToken);
        var request = new CustodianElectionRequest(
            connection, safekeeping.AccountNumber, action.PublicId, action.PartyActionId!, instrument.Symbol,
            instrument.QuoteCurrency.Trim(), action.ElectionQuantity!.Value);

        action.RecordElectionAttempt(now);
        await db.SaveChangesAsync(cancellationToken);

        var result = await elections.SubmitAsync(request, cancellationToken);
        try
        {
            await ApplyAsync(action, result, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A receipt took the dividend meanwhile; the outcome is applied to the stored row if it still waits.
            db.ClearTrackedChanges();
            var current = await db.CorporateActions.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
            if (current.ElectionStatus == ReinvestmentElectionStatus.Pending)
            {
                await ApplyAsync(current, result, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return result;
    }

    private async Task ApplyAsync(CorporateAction action, CustodianElectionResult result, CancellationToken cancellationToken)
    {
        if (result.IsAccepted)
        {
            action.AcceptElection();
        }
        else if (result.RejectReason is { } reason)
        {
            action.RejectElection(reason);
            await PayDividend.TryAsync(db, action, time.GetUtcNow(), cancellationToken);
        }
    }
}
