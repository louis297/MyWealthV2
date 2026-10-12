using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.NetWorth;

internal static class NetWorthReader
{
    public static async Task<NetWorthDto> ReadAsync(
        IApplicationDbContext db,
        IMarketData marketData,
        TimeProvider time,
        int tenantId,
        int? customerId,
        int? assignedAdviserId,
        CancellationToken cancellationToken)
    {
        var accounts = await (
            from account in db.Accounts.AsNoTracking()
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.TenantId == tenantId
                  && account.IsActive
                  && (customerId == null || account.CustomerId == customerId)
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new AccountRow(account.Id, account.Currency, account.Type, customer.PublicId, customer.Name)
        ).ToListAsync(cancellationToken);

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
                holding.AccountId,
                instrument.PublicId,
                instrument.Symbol,
                holding.InstrumentId,
                holding.Quantity,
                instrument.QuoteCurrency)
        ).ToListAsync(cancellationToken);

        var priced = holdings.Select(holding => Price(holding, marketData)).ToList();

        return new NetWorthDto
        {
            AsOf = time.GetUtcNow(),
            Currencies = CurrenciesFor(accounts, priced, cashByAccount),
            Breakdowns = new NetWorthBreakdownsDto
            {
                ByCustomer = accounts
                    .GroupBy(account => account.CustomerId)
                    .Select(group => new NetWorthCustomerBreakdownDto
                    {
                        CustomerId = group.Key,
                        Name = group.First().CustomerName,
                        Currencies = CurrenciesFor(group, priced, cashByAccount)
                    })
                    .OrderBy(row => row.Name, StringComparer.Ordinal)
                    .ThenBy(row => row.CustomerId)
                    .ToList(),
                ByAccountType = accounts
                    .GroupBy(account => account.Type)
                    .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
                    .Select(group => new NetWorthAccountTypeBreakdownDto
                    {
                        Type = group.Key.ToString(),
                        Currencies = CurrenciesFor(group, priced, cashByAccount)
                    })
                    .ToList(),
                TopHoldings = priced
                    .GroupBy(holding => holding.InstrumentId)
                    .Select(group => new NetWorthTopHoldingDto
                    {
                        InstrumentId = group.Key,
                        Symbol = group.First().Symbol,
                        Quantity = group.Sum(holding => holding.Quantity),
                        MarketValue = group.Sum(holding => holding.MarketValue),
                        Currency = group.First().Currency
                    })
                    .OrderByDescending(holding => holding.MarketValue)
                    .ThenBy(holding => holding.Symbol, StringComparer.Ordinal)
                    .ThenBy(holding => holding.InstrumentId)
                    .ToList()
            }
        };
    }

    private static PricedHolding Price(HoldingRow holding, IMarketData marketData)
    {
        var price = marketData.TryGetPrice(holding.InstrumentInternalId, null);
        return new PricedHolding(
            holding.AccountId,
            holding.InstrumentId,
            holding.Symbol,
            holding.Quantity,
            price is null ? 0m : holding.Quantity * price.Amount,
            (price?.Currency ?? holding.QuoteCurrency).Trim());
    }

    private static IReadOnlyList<NetWorthCurrencyDto> CurrenciesFor(
        IEnumerable<AccountRow> accounts,
        IReadOnlyList<PricedHolding> priced,
        IReadOnlyDictionary<int, decimal> cashByAccount)
    {
        var rows = accounts.ToList();
        var ids = rows.Select(account => account.Id).ToHashSet();
        var totals = new SortedDictionary<string, MutableTotal>(StringComparer.Ordinal);
        foreach (var account in rows)
        {
            Add(totals, account.Currency, cashByAccount.GetValueOrDefault(account.Id), 0m);
        }

        foreach (var holding in priced)
        {
            if (ids.Contains(holding.AccountId))
            {
                Add(totals, holding.Currency, 0m, holding.MarketValue);
            }
        }

        return totals.Select(pair => ToCurrency(pair.Key, pair.Value)).ToList();
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

    private sealed record AccountRow(int Id, string Currency, AccountType Type, Guid CustomerId, string CustomerName);

    private sealed record HoldingRow(
        int AccountId,
        Guid InstrumentId,
        string Symbol,
        int InstrumentInternalId,
        decimal Quantity,
        string QuoteCurrency);

    private sealed record PricedHolding(
        int AccountId,
        Guid InstrumentId,
        string Symbol,
        decimal Quantity,
        decimal MarketValue,
        string Currency);

    private sealed class MutableTotal
    {
        public decimal Cash { get; set; }

        public decimal Holdings { get; set; }
    }
}
