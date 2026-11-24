using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>What a processing run did: announcements processed, skipped, and failed (left <c>Received</c>).</summary>
public sealed record AnnouncementProcessingOutcome(int Processed, int Skipped, int Failed);

/// <summary>
/// Turns the stored announcements of one market data connection into corporate actions on every Tenant's instrument
/// with the symbol and quote currency (docs/features/corporate-actions.md R11–R13; ADR 0021 rule 8). Each
/// announcement, oldest first, is processed in its own database transaction that also sets its status. Reads past the
/// tenant query filter and names each Tenant itself.
/// </summary>
public sealed class ProcessCorporateActionAnnouncements(
    IApplicationDbContext db,
    MarketDataConnections connections,
    TimeProvider time,
    ILogger<ProcessCorporateActionAnnouncements> logger)
{
    public async Task<AnnouncementProcessingOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        var served = connections.Named(connection);
        if (served is null)
        {
            logger.LogWarning("Market data connection {Connection} is not configured on this host; nothing is processed.", connection);
            return new AnnouncementProcessingOutcome(0, 0, 0);
        }

        db.ClearTrackedChanges();
        var ids = await db.CorporateActionAnnouncements.AsNoTracking()
            .Where(row => row.Connection == connection && row.Status == AnnouncementStatus.Received)
            .OrderBy(row => row.ReceivedAt)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var (processed, skipped, failed) = (0, 0, 0);
        foreach (var id in ids)
        {
            db.ClearTrackedChanges();
            try
            {
                var status = AnnouncementStatus.Received;
                await db.ExecuteInTransactionAsync(async token =>
                {
                    var announcement = await db.CorporateActionAnnouncements.SingleAsync(row => row.Id == id, token);
                    if (announcement.Status != AnnouncementStatus.Received)
                    {
                        status = AnnouncementStatus.Received;
                        return;
                    }

                    var reason = await ProcessAsync(announcement, served, token);
                    if (reason is null)
                    {
                        announcement.MarkProcessed(time.GetUtcNow());
                    }
                    else
                    {
                        announcement.Skip(reason.Value, time.GetUtcNow());
                    }

                    await db.SaveChangesAsync(token);
                    status = announcement.Status;
                }, cancellationToken);
                if (status == AnnouncementStatus.Processed)
                {
                    processed++;
                }
                else if (status == AnnouncementStatus.Skipped)
                {
                    skipped++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogError(exception, "Corporate action announcement {AnnouncementId} could not be processed; the next run tries again.", id);
            }
        }

        return new AnnouncementProcessingOutcome(processed, skipped, failed);
    }

    /// <summary>R11, R12: applies the announcement to every matching instrument. Null when it created or changed at
    /// least one corporate action, else the first skip reason that applies.</summary>
    private async Task<AnnouncementSkipReason?> ProcessAsync(
        CorporateActionAnnouncement announcement,
        MarketDataConnection served,
        CancellationToken cancellationToken)
    {
        if (announcement.Kind is not { } kind)
        {
            return AnnouncementSkipReason.UnsupportedKind;
        }

        // Dividends R9: a dividend's terms break R3 → Invalid; another kind needs a ratio that fits it.
        var isDividend = kind == CorporateActionKind.Dividend;
        var ratio = new CorporateActionRatio(announcement.RatioNew ?? 0, announcement.RatioHeld ?? 0);
        DividendTerms? terms = null;
        if (isDividend)
        {
            if (DividendTerms.RefusalOf(announcement.AmountPerShare, announcement.TaxSource, announcement.ImputationCreditPerShare,
                    announcement.ForeignTaxRatePercent, announcement.ReinvestmentPrice) is not null)
            {
                return AnnouncementSkipReason.Invalid;
            }

            terms = DividendTerms.Create(announcement.AmountPerShare!.Value, announcement.TaxSource!.Value,
                announcement.ImputationCreditPerShare, announcement.ForeignTaxRatePercent, announcement.ReinvestmentPrice);
        }

        if ((!isDividend && !ratio.IsValidFor(kind)) || !served.Serves(announcement.Symbol))
        {
            return AnnouncementSkipReason.Invalid;
        }

        // R12, R13: every Instrument of any Tenant, active or not, disabled Tenants included.
        var symbol = announcement.Symbol.ToUpperInvariant();
        var currency = announcement.Currency.Trim();
        var instruments = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Symbol.ToUpper() == symbol && row.QuoteCurrency == currency)
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.TenantId })
            .ToListAsync(cancellationToken);
        if (instruments.Count == 0)
        {
            return AnnouncementSkipReason.NoInstrument;
        }

        var instrumentIds = instruments.Select(row => row.Id).ToList();
        var actions = await db.CorporateActions.IgnoreQueryFilters()
            .Where(action => action.Origin == CorporateActionOrigin.Feed
                             && action.Connection == announcement.Connection
                             && action.PartyActionId == announcement.PartyActionId
                             && instrumentIds.Contains(action.InstrumentId))
            .ToDictionaryAsync(action => action.InstrumentId, cancellationToken);

        var changed = false;
        var alreadyEffective = false;
        var now = time.GetUtcNow();
        foreach (var instrument in instruments)
        {
            actions.TryGetValue(instrument.Id, out var action);
            if (action is null)
            {
                if (announcement.PartyStatus == PartyActionStatus.Cancelled)
                {
                    continue;
                }

                action = terms is not null
                    ? CorporateAction.AnnounceDividend(
                        instrument.TenantId, instrument.Id, announcement.Connection, announcement.PartyActionId, announcement.Version,
                        terms, announcement.ExDate, announcement.RecordDate, announcement.PaymentDate)
                    : CorporateAction.Announce(
                        instrument.TenantId, instrument.Id, announcement.Connection, announcement.PartyActionId, announcement.Version,
                        kind, ratio, announcement.ExDate, announcement.RecordDate, announcement.PaymentDate);
                action.CreatedBy = SystemActor.Name;
                db.CorporateActions.Add(action);
                await db.SaveChangesAsync(cancellationToken);
                if (announcement.PartyStatus == PartyActionStatus.Effective)
                {
                    await CorporateActionEffect.MakeEffectiveAsync(db, action, now, null, cancellationToken);
                }

                changed = true;
                continue;
            }

            // R12: the kind of a corporate action never changes; another kind changes nothing.
            if (action.Kind != kind)
            {
                continue;
            }

            if (action.Status != CorporateActionStatus.Announced)
            {
                alreadyEffective = true;
                continue;
            }

            if (announcement.Version <= action.FeedVersion)
            {
                continue;
            }

            switch (announcement.PartyStatus)
            {
                case PartyActionStatus.Announced:
                    Amend(action, announcement, ratio, terms);
                    break;
                case PartyActionStatus.Effective:
                    Amend(action, announcement, ratio, terms);
                    await CorporateActionEffect.MakeEffectiveAsync(db, action, now, null, cancellationToken);
                    break;
                case PartyActionStatus.Cancelled:
                    action.CancelFromFeed(announcement.Version, now);
                    break;
            }

            changed = true;
        }

        if (changed)
        {
            return null;
        }

        return alreadyEffective ? AnnouncementSkipReason.AlreadyEffective : AnnouncementSkipReason.Superseded;
    }

    /// <summary>R12 and dividends R9: a later version replaces the ratio or the terms, and the dates.</summary>
    private static void Amend(
        CorporateAction action, CorporateActionAnnouncement announcement, CorporateActionRatio ratio, DividendTerms? terms)
    {
        if (terms is not null)
        {
            action.AmendDividend(announcement.Version, terms, announcement.ExDate, announcement.RecordDate, announcement.PaymentDate);
        }
        else
        {
            action.Amend(announcement.Version, ratio, announcement.ExDate, announcement.RecordDate, announcement.PaymentDate);
        }
    }
}
