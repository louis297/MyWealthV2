using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>What one apply run did: entitlements posted, not applied, and failed (left <c>Pending</c>).</summary>
public sealed record ApplyCorporateActionsOutcome(int Posted, int NotApplied, int Failed)
{
    public bool HasFailures => Failed > 0;
}

/// <summary>
/// The apply step (docs/features/corporate-actions.md R23–R25, R28–R31): makes every due <c>Manual</c> corporate
/// action effective, then posts each <c>Pending</c> entitlement of every <c>Effective</c> action, oldest
/// <c>effectiveAt</c> first and in account <c>Id</c> order, each in its own database transaction under the Account
/// lock. The one place an entitlement is posted. Runs as the worker's system user and names each Tenant itself.
/// </summary>
public sealed class ApplyCorporateActions(
    IApplicationDbContext db,
    IInstrumentPriceStore prices,
    MarketDataConnections connections,
    TimeProvider time,
    ILogger<ApplyCorporateActions> logger)
{
    /// <summary>With <paramref name="connection"/>, only the instruments it serves (R8 step 3); without, everything
    /// (the job <c>corporate-action-apply</c>).</summary>
    public async Task<ApplyCorporateActionsOutcome> RunAsync(string? connection = null, CancellationToken cancellationToken = default)
    {
        var served = connection is null ? null : connections.Named(connection);
        if (connection is not null && served is null)
        {
            logger.LogWarning("Market data connection {Connection} is not configured on this host; nothing is applied.", connection);
            return new ApplyCorporateActionsOutcome(0, 0, 0);
        }

        var failed = await MakeDueEffectiveAsync(served, cancellationToken);
        var (posted, notApplied, failedPostings) = await PostPendingAsync(served, cancellationToken);
        return new ApplyCorporateActionsOutcome(posted, notApplied, failed + failedPostings);
    }

    /// <summary>R23: a <c>Manual</c> action that is <c>Announced</c> is due when a price of its instrument is stored
    /// for its ex-date or a later date.</summary>
    private async Task<int> MakeDueEffectiveAsync(MarketDataConnection? served, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var candidates = await (
                from action in db.CorporateActions.IgnoreQueryFilters().AsNoTracking()
                join instrument in db.Instruments.IgnoreQueryFilters().AsNoTracking() on action.InstrumentId equals instrument.Id
                where action.Origin == CorporateActionOrigin.Manual && action.Status == CorporateActionStatus.Announced
                orderby action.ExDate, action.Id
                select new { action.Id, action.PublicId, action.InstrumentId, action.ExDate, instrument.Symbol })
            .ToListAsync(cancellationToken);

        var failed = 0;
        foreach (var candidate in candidates.Where(row => served is null || served.Serves(row.Symbol)))
        {
            if (!await prices.HasPriceFromAsync(candidate.InstrumentId, candidate.ExDate, cancellationToken))
            {
                continue;
            }

            db.ClearTrackedChanges();
            try
            {
                await db.ExecuteInTransactionAsync(async token =>
                {
                    var action = await db.CorporateActions.IgnoreQueryFilters().SingleAsync(row => row.Id == candidate.Id, token);
                    if (action.Status != CorporateActionStatus.Announced)
                    {
                        return;
                    }

                    await CorporateActionEffect.MakeEffectiveAsync(db, action, time.GetUtcNow(), null, token);
                    await db.SaveChangesAsync(token);
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogError(exception, "Corporate action {CorporateActionId} could not be made effective; the next run tries again.",
                    candidate.PublicId);
            }
        }

        return failed;
    }

    private async Task<(int Posted, int NotApplied, int Failed)> PostPendingAsync(
        MarketDataConnection? served,
        CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var effective = await (
                from action in db.CorporateActions.IgnoreQueryFilters().AsNoTracking()
                join instrument in db.Instruments.IgnoreQueryFilters().AsNoTracking() on action.InstrumentId equals instrument.Id
                where action.Status == CorporateActionStatus.Effective
                orderby action.EffectiveAt, action.Id
                select new { Action = action, instrument.Symbol, instrument.QuoteCurrency })
            .ToListAsync(cancellationToken);

        var (posted, notApplied, failed) = (0, 0, 0);
        foreach (var row in effective.Where(item => served is null || served.Serves(item.Symbol)))
        {
            var action = row.Action;
            var pending = await db.CorporateActionEntitlements.IgnoreQueryFilters().AsNoTracking()
                .Where(entitlement => entitlement.CorporateActionId == action.Id && entitlement.Status == EntitlementStatus.Pending)
                .OrderBy(entitlement => entitlement.AccountId)
                .Select(entitlement => new { entitlement.Id, entitlement.AccountId })
                .ToListAsync(cancellationToken);

            var failedHere = 0;
            foreach (var entitlement in pending)
            {
                db.ClearTrackedChanges();
                try
                {
                    var result = await PostOneAsync(action, row.QuoteCurrency.Trim(), entitlement.Id, entitlement.AccountId, cancellationToken);
                    if (result == EntitlementStatus.Posted)
                    {
                        posted++;
                    }
                    else if (result == EntitlementStatus.NotApplied)
                    {
                        notApplied++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failedHere++;
                    var accountPublicId = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
                        .Where(account => account.Id == entitlement.AccountId)
                        .Select(account => account.PublicId)
                        .SingleOrDefaultAsync(cancellationToken);
                    logger.LogError(exception,
                        "Corporate action {CorporateActionId} could not be posted on account {AccountId}; the next run tries again.",
                        action.PublicId, accountPublicId);
                }
            }

            failed += failedHere;
            await MarkAppliedWhenDoneAsync(action.Id, cancellationToken);
        }

        return (posted, notApplied, failed);
    }

    /// <summary>R25: one entitlement, under the Account lock. The entitlement and the holding are read again.</summary>
    private async Task<EntitlementStatus?> PostOneAsync(
        CorporateAction action,
        string quoteCurrency,
        long entitlementId,
        int accountId,
        CancellationToken cancellationToken)
    {
        EntitlementStatus? outcome = null;
        await db.ExecuteInTransactionAsync(async token =>
        {
            outcome = null;
            await db.LockAccountAsync(accountId, token);
            var entitlement = await db.CorporateActionEntitlements.IgnoreQueryFilters()
                .SingleAsync(row => row.Id == entitlementId, token);
            if (entitlement.Status != EntitlementStatus.Pending)
            {
                return;
            }

            var now = time.GetUtcNow();
            var holding = await db.Holdings.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(row => row.AccountId == accountId && row.InstrumentId == action.InstrumentId, token);
            if (holding is null || holding.Quantity <= 0m)
            {
                entitlement.MarkNotApplied(EntitlementReason.NoHolding, now);
                await db.SaveChangesAsync(token);
                outcome = EntitlementStatus.NotApplied;
                return;
            }

            var before = holding.Quantity;
            var after = action.Ratio.Apply(action.Kind, before);
            if (after == 0m || after == before)
            {
                entitlement.MarkNotApplied(EntitlementReason.TooSmall, now);
                await db.SaveChangesAsync(token);
                outcome = EntitlementStatus.NotApplied;
                return;
            }

            var posting = Transaction.PostCorporateAction(
                action.TenantId, accountId, action.InstrumentId, quoteCurrency, action.Kind, action.Ratio, action.ExDate,
                action.RecordDate, action.PaymentDate, before, after, now, TransactionSource.CorporateAction(action.PublicId));
            posting.CreatedBy = SystemActor.Name;
            foreach (var leg in posting.SecurityLegs)
            {
                leg.CreatedBy = SystemActor.Name;
            }

            db.Transactions.Add(posting);
            await db.SaveChangesAsync(token);
            await HoldingProjection.SyncAsync(db, action.TenantId, accountId, [action.InstrumentId], token);
            entitlement.MarkPosted(before, after, posting.Id, now);
            await db.SaveChangesAsync(token);
            outcome = EntitlementStatus.Posted;
        }, cancellationToken);
        return outcome;
    }

    /// <summary>R24: an <c>Effective</c> action with no <c>Pending</c> entitlement left is <c>Applied</c>.</summary>
    private async Task MarkAppliedWhenDoneAsync(int actionId, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var anyPending = await db.CorporateActionEntitlements.IgnoreQueryFilters().AnyAsync(
            row => row.CorporateActionId == actionId && row.Status == EntitlementStatus.Pending, cancellationToken);
        if (anyPending)
        {
            return;
        }

        var action = await db.CorporateActions.IgnoreQueryFilters().SingleAsync(row => row.Id == actionId, cancellationToken);
        if (action.Status == CorporateActionStatus.Effective)
        {
            action.MarkApplied(time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
