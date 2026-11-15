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
                leg.Id,
                leg.TransactionId,
                AccountId = account.PublicId,
                CustomerId = customer.PublicId,
                leg.Role,
                leg.Amount,
                leg.Currency
            }).ToListAsync(cancellationToken);

        var feeRows = await (
            from detail in db.TransactionFeeDetails.AsNoTracking()
            join schedule in db.TradeFeeSchedules.AsNoTracking() on detail.TradeFeeScheduleId equals schedule.Id into schedules
            from schedule in schedules.DefaultIfEmpty()
            where transactionIds.Contains(detail.TransactionId)
            select new
            {
                detail.CashLegId,
                detail.Kind,
                ScheduleId = schedule == null ? (Guid?)null : schedule.PublicId,
                detail.BasisAmount,
                detail.FeeAmount,
                detail.Currency
            }).ToDictionaryAsync(row => row.CashLegId, cancellationToken);

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

        var fxRows = await db.Transactions.AsNoTracking()
            .Where(transaction => transactionIds.Contains(transaction.Id) && transaction.FxDetail != null)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.FxDetail!.BaseCurrency,
                transaction.FxDetail.QuoteCurrency,
                transaction.FxDetail.Rate,
                transaction.FxDetail.RateAsOfDate
            }).ToListAsync(cancellationToken);

        return new TransactionLegs(
            cashRows.ToLookup(row => row.TransactionId, row => new TransactionCashDto
            {
                AccountId = row.AccountId,
                CustomerId = row.CustomerId,
                Role = row.Role.ToString(),
                Amount = row.Amount,
                Currency = row.Currency.Trim(),
                Fee = feeRows.TryGetValue(row.Id, out var fee)
                    ? new TransactionFeeDto
                    {
                        Kind = fee.Kind.ToString(),
                        ScheduleId = fee.ScheduleId,
                        BasisAmount = fee.BasisAmount,
                        Amount = fee.FeeAmount,
                        Currency = fee.Currency.Trim()
                    }
                    : null
            }),
            securityRows.ToLookup(row => row.TransactionId, row => new TransactionSecurityDto
            {
                AccountId = row.AccountId,
                CustomerId = row.CustomerId,
                InstrumentId = row.InstrumentId,
                Quantity = row.Quantity,
                Cost = row.CostAmount,
                CostCurrency = row.CostCurrency.Trim()
            }),
            fxRows.ToDictionary(row => row.Id, row => new TransactionFxDto
            {
                BaseCurrency = row.BaseCurrency.Trim(),
                QuoteCurrency = row.QuoteCurrency.Trim(),
                Rate = row.Rate,
                RateAsOfDate = row.RateAsOfDate.ToString("yyyy-MM-dd")
            }));
    }
}

internal sealed record TransactionLegs(
    ILookup<int, TransactionCashDto> Cash,
    ILookup<int, TransactionSecurityDto> Security,
    IReadOnlyDictionary<int, TransactionFxDto> Fx);
