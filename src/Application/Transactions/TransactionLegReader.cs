using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Transactions;

/// <summary>Reads the legs on visible Accounts as item DTOs, each with its leg account and Customer.</summary>
internal static class TransactionLegReader
{
    public static async Task<TransactionLegs> LoadAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> transactionIds,
        IQueryable<int> visibleAccountIds,
        CancellationToken cancellationToken)
    {
        var cashRows = await (
            from leg in db.TransactionCashLegs.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on leg.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where transactionIds.Contains(leg.TransactionId) && visibleAccountIds.Contains(leg.AccountId)
            orderby leg.Id
            select new
            {
                leg.TransactionId,
                AccountId = account.PublicId,
                CustomerId = customer.PublicId,
                leg.Amount,
                leg.Currency
            }).ToListAsync(cancellationToken);

        var securityRows = await (
            from leg in db.TransactionSecurityLegs.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on leg.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            join instrument in db.Instruments.AsNoTracking() on leg.InstrumentId equals instrument.Id
            where transactionIds.Contains(leg.TransactionId) && visibleAccountIds.Contains(leg.AccountId)
            orderby leg.Id
            select new
            {
                leg.TransactionId,
                AccountId = account.PublicId,
                CustomerId = customer.PublicId,
                InstrumentId = instrument.PublicId,
                leg.Quantity,
                leg.CostAmount,
                leg.CostCurrency
            }).ToListAsync(cancellationToken);

        return new TransactionLegs(
            cashRows.ToLookup(row => row.TransactionId, row => new TransactionCashDto
            {
                AccountId = row.AccountId,
                CustomerId = row.CustomerId,
                Amount = row.Amount,
                Currency = row.Currency.Trim()
            }),
            securityRows.ToLookup(row => row.TransactionId, row => new TransactionSecurityDto
            {
                AccountId = row.AccountId,
                CustomerId = row.CustomerId,
                InstrumentId = row.InstrumentId,
                Quantity = row.Quantity,
                Cost = row.CostAmount,
                CostCurrency = row.CostCurrency.Trim()
            }));
    }
}

internal sealed record TransactionLegs(
    ILookup<int, TransactionCashDto> Cash,
    ILookup<int, TransactionSecurityDto> Security);
