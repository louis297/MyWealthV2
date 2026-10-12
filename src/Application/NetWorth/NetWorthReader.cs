using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.NetWorth;

internal static class NetWorthReader
{
    public static async Task<NetWorthDto> ReadAsync(
        IApplicationDbContext db,
        IMarketData marketData,
        TimeProvider time,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(account => account.TenantId == tenantId && account.IsActive)
            .Select(account => new AccountRow(account.Id, account.Currency))
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return NetWorthDto.Empty(time.GetUtcNow());
        }

        var accountIds = accounts.Select(account => account.Id).ToList();
        var cashByAccount = await (
            from leg in db.TransactionCashLegs.AsNoTracking()
            join transaction in db.Transactions.AsNoTracking() on leg.TransactionId equals transaction.Id
            where accountIds.Contains(transaction.AccountId)
            group leg by transaction.AccountId
            into grouped
            select new { AccountId = grouped.Key, Sum = grouped.Sum(leg => leg.Amount) }
        ).ToDictionaryAsync(row => row.AccountId, row => row.Sum, cancellationToken);

        var holdings = await (
            from holding in db.Holdings.AsNoTracking()
            join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
            where accountIds.Contains(holding.AccountId) && holding.Quantity > 0
            select new HoldingRow(
                holding.InstrumentId,
                holding.Quantity,
                instrument.QuoteCurrency)
        ).ToListAsync(cancellationToken);

        var totals = new SortedDictionary<string, MutableTotal>(StringComparer.Ordinal);
        foreach (var account in accounts)
        {
            Add(totals, account.Currency, cashByAccount.GetValueOrDefault(account.Id), 0m);
        }

        foreach (var holding in holdings)
        {
            var price = marketData.TryGetPrice(holding.InstrumentId, null);
            var currency = price?.Currency ?? holding.QuoteCurrency;
            var marketValue = price is null ? 0m : holding.Quantity * price.Amount;
            Add(totals, currency, 0m, marketValue);
        }

        return new NetWorthDto
        {
            AsOf = time.GetUtcNow(),
            Currencies = totals.Select(pair => ToCurrency(pair.Key, pair.Value)).ToList(),
            Breakdowns = EmptyBreakdowns()
        };
    }

    private static void Add(SortedDictionary<string, MutableTotal> totals, string currency, decimal cash, decimal holdings)
    {
        var code = currency.Trim();
        if (!totals.TryGetValue(code, out var total))
        {
            total = new MutableTotal();
            totals[code] = total;
        }

        total.Cash += cash;
        total.Holdings += holdings;
    }

    private static NetWorthCurrencyDto ToCurrency(string currency, MutableTotal total)
    {
        var assets = total.Cash + total.Holdings;
        return new NetWorthCurrencyDto
        {
            Currency = currency,
            Assets = new NetWorthAssetsDto
            {
                Cash = total.Cash,
                Holdings = total.Holdings,
                Total = assets
            },
            Liabilities = new NetWorthLiabilitiesDto { Total = 0m },
            Net = assets
        };
    }

    private static NetWorthBreakdownsDto EmptyBreakdowns() => new()
    {
        ByCustomer = [],
        ByAccountType = [],
        TopHoldings = []
    };

    private sealed record AccountRow(int Id, string Currency);

    private sealed record HoldingRow(int InstrumentId, decimal Quantity, string QuoteCurrency);

    private sealed class MutableTotal
    {
        public decimal Cash { get; set; }

        public decimal Holdings { get; set; }
    }
}
