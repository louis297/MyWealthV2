using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Valuations;

/// <summary>The position of a day and the basis changes of its lines (account-valuation R3–R5, R18).</summary>
public interface IAccountPositions
{
    Task<AccountPositionRead> ReadAsync(Account account, DateOnly day, CancellationToken cancellationToken);
}

/// <summary>The position of a day with the entitlement postings that R18 compares with its prices.</summary>
public sealed record AccountPositionRead(AccountPosition Position, IReadOnlyList<BasisChange> BasisChanges);

/// <summary>
/// Account valuation R3–R5: the cash legs and the security legs on the Account booked before the day ended, each
/// Transaction from its own <c>bookedAt</c>, a Reversal too. Reads ignore the tenant filter and name the Account's
/// tenant themselves, so that a <c>worker</c> run can call this with no current tenant.
/// </summary>
public class AccountPositions(IApplicationDbContext db) : IAccountPositions
{
    public async Task<AccountPositionRead> ReadAsync(Account account, DateOnly day, CancellationToken cancellationToken)
    {
        var end = LedgerDay.End(day);

        // R3: every role, reserved and unsettled cash in full; not rounded.
        var cash = await (
                from leg in db.TransactionCashLegs.IgnoreQueryFilters()
                join transaction in db.Transactions.IgnoreQueryFilters() on leg.TransactionId equals transaction.Id
                where leg.TenantId == account.TenantId && leg.AccountId == account.Id && transaction.BookedAt < end
                select (decimal?)leg.Amount)
            .SumAsync(cancellationToken) ?? 0m;

        // R4: the ledger quantity of each Instrument; a sum of 0 is left out.
        var quantities = await (
                from leg in db.TransactionSecurityLegs.IgnoreQueryFilters()
                join transaction in db.Transactions.IgnoreQueryFilters() on leg.TransactionId equals transaction.Id
                where leg.TenantId == account.TenantId && leg.AccountId == account.Id && transaction.BookedAt < end
                group leg by leg.InstrumentId into grouped
                select new { InstrumentId = grouped.Key, Quantity = grouped.Sum(leg => leg.Quantity) })
            .ToListAsync(cancellationToken);
        var lines = quantities
            .Where(row => row.Quantity != 0m)
            .OrderBy(row => row.InstrumentId)
            .Select(row => new PositionLine(row.InstrumentId, row.Quantity))
            .ToList();

        return new AccountPositionRead(new AccountPosition(day, cash, lines), []);
    }
}
