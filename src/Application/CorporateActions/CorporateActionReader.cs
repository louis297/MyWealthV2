using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>Scope, item and refusals of corporate actions (corporate-actions R1, R21, R35).</summary>
internal static class CorporateActionReader
{
    /// <summary>R1: the corporate actions of the caller's Tenant.</summary>
    public static IQueryable<CorporateAction> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        return db.CorporateActions.Where(action => action.TenantId == tenantId);
    }

    /// <summary>R1: the action in the caller's Tenant, tracked; else 404.</summary>
    public static async Task<CorporateAction> FindAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        CancellationToken cancellationToken) =>
        await Scope(db, currentUser).SingleOrDefaultAsync(action => action.PublicId == id, cancellationToken)
        ?? throw new NotFoundException("CorporateAction", id);

    /// <summary>R21: a <c>Feed</c> action, or one that is not <c>Announced</c>, refuses a change by hand; then a stale
    /// <c>rowVersion</c> is 409.</summary>
    public static void EnsureChangeable(CorporateAction action, string? rowVersion, string verb)
    {
        if (action.Origin == CorporateActionOrigin.Feed)
        {
            throw Error("Origin", "This corporate action comes from a market data connection and cannot be changed by hand.");
        }

        if (action.Status != CorporateActionStatus.Announced)
        {
            throw Error("Status", $"This corporate action is {action.Status} and cannot be {verb}.");
        }

        if (!string.Equals(Convert.ToBase64String(action.RowVersion ?? []), rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }
    }

    public static async Task SaveAsync(IApplicationDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }

    public static ValidationException Error(string property, string message) =>
        new([new ValidationFailure(property, message)]);

    /// <summary>R35: the items of <paramref name="rows"/>, with the entitlement counts the caller can see (R36).</summary>
    public static async Task<List<CorporateActionDto>> ToDtosAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IReadOnlyList<CorporateAction> rows,
        CancellationToken cancellationToken)
    {
        var instrumentIds = rows.Select(row => row.InstrumentId).Distinct().ToList();
        var instruments = await db.Instruments.AsNoTracking()
            .Where(row => instrumentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new { Dto = new CorporateActionInstrumentDto(row.PublicId, row.Symbol, row.Name), row.QuoteCurrency }, cancellationToken);

        var userIds = rows.SelectMany(row => new[] { row.EffectiveByUserId, row.CancelledByUserId })
            .OfType<int>()
            .Distinct()
            .ToList();
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);

        // CreatedBy holds the person's identity user id; the platform writes its system name, which matches no one.
        var creators = rows.Select(row => row.CreatedBy).OfType<string>().Distinct().ToList();
        var tenantIds = rows.Select(row => row.TenantId).Distinct().ToList();
        var creatorRefs = await db.Users.AsNoTracking()
            .Where(row => row.TenantId != null && tenantIds.Contains(row.TenantId.Value) && creators.Contains(row.IdentityUserId))
            .ToDictionaryAsync(row => row.IdentityUserId, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);

        var actionIds = rows.Select(row => row.Id).ToList();

        // Dividends R15: the expected amount counts every entitlement of the dividend, whoever the caller is.
        var dividendIds = rows.Where(row => row.Kind == CorporateActionKind.Dividend).Select(row => row.Id).ToList();
        var receiptIds = rows.Where(row => row.ReceiptLineId is not null).Select(row => row.ReceiptLineId!.Value).ToList();
        var receipts = receiptIds.Count == 0 || currentUser.Role != UserRole.TenantAdmin
            ? new Dictionary<int, Guid>()
            : await db.BankStatementLines.AsNoTracking()
                .Where(row => receiptIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var dividendEntitlements = dividendIds.Count == 0
            ? []
            : await db.CorporateActionEntitlements.AsNoTracking()
                .Where(row => dividendIds.Contains(row.CorporateActionId))
                .ToListAsync(cancellationToken);
        var visibleAccounts = AccountScope.ManagedAccounts(db, currentUser);
        var counts = await db.CorporateActionEntitlements.AsNoTracking()
            .Where(row => actionIds.Contains(row.CorporateActionId) && visibleAccounts.Any(account => account.Id == row.AccountId))
            .GroupBy(row => new { row.CorporateActionId, row.Status })
            .Select(group => new { group.Key.CorporateActionId, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            int Count(EntitlementStatus status) =>
                counts.Where(item => item.CorporateActionId == row.Id && item.Status == status).Sum(item => item.Count);
            var posted = Count(EntitlementStatus.Posted);
            var pending = Count(EntitlementStatus.Pending);
            var notApplied = Count(EntitlementStatus.NotApplied);
            return new CorporateActionDto
            {
                Id = row.PublicId,
                Instrument = instruments[row.InstrumentId].Dto,
                Kind = row.Kind.ToString(),
                RatioNew = row.RatioNew,
                RatioHeld = row.RatioHeld,
                ExDate = row.ExDate,
                RecordDate = row.RecordDate,
                PaymentDate = row.PaymentDate,
                Origin = row.Origin.ToString(),
                Status = row.Status.ToString(),
                Memo = row.Memo,
                CreatedBy = row.CreatedBy is not null ? creatorRefs.GetValueOrDefault(row.CreatedBy) : null,
                CreatedAt = row.Created,
                EffectiveAt = row.EffectiveAt,
                EffectiveBy = row.EffectiveByUserId is int effectiveBy ? users.GetValueOrDefault(effectiveBy) : null,
                AppliedAt = row.AppliedAt,
                CancelledAt = row.CancelledAt,
                CancelledBy = row.CancelledByUserId is int cancelledBy ? users.GetValueOrDefault(cancelledBy) : null,
                CancelNote = row.CancelNote,
                Payment = row.Kind == CorporateActionKind.Dividend && row.EffectiveAt is not null
                    ? Payment(row, DividendPayment.ExpectedAmount(dividendEntitlements.Where(item => item.CorporateActionId == row.Id), 0m),
                        instruments[row.InstrumentId].QuoteCurrency.Trim(),
                        row.ReceiptLineId is int lineId ? receipts.GetValueOrDefault(lineId) : null)
                    : null,
                Election = row.Kind == CorporateActionKind.Dividend && row.ReinvestmentPrice is not null && row.ElectionStatus is { } election
                    ? new CorporateActionElectionDto(election.ToString(), row.ElectionQuantity, 0, null, row.SharesReceived, null,
                        row.ReleasedAt, null)
                    : null,
                Dividend = row.Terms is { } terms
                    ? new CorporateActionDividendDto(
                        terms.AmountPerShare, instruments[row.InstrumentId].QuoteCurrency.Trim(), terms.TaxSource.ToString(),
                        terms.ImputationCreditPerShare, terms.ForeignTaxRatePercent, terms.ReinvestmentPrice)
                    : null,
                Entitlements = new CorporateActionEntitlementCountsDto(posted + pending + notApplied, posted, pending, notApplied),
                RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
            };
        }).ToList();
    }

    /// <summary>Dividends R49: the payment block; the statement line only for a TenantAdmin.</summary>
    private static CorporateActionPaymentDto Payment(CorporateAction row, decimal expected, string currency, Guid? statementLineId) =>
        new(expected, row.ReceivedAmount, row.ReceivedAmount - expected, currency,
            statementLineId == Guid.Empty ? null : statementLineId, row.PaidAt);
}
