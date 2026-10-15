namespace MyWealthV2.Application.Reports;

public sealed record AdviserBalanceAccount(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string AccountType,
    string Currency,
    decimal CashBalance,
    bool Closed,
    IReadOnlyList<AdviserBalanceHolding> Holdings);

public sealed record AdviserBalanceHolding(
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal CostAmount,
    string CostCurrency,
    decimal? PriceAmount,
    string? PriceCurrency,
    string QuoteCurrency);

public abstract record AdviserBalanceLine(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName);

public sealed record AdviserCashLine(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string AccountType,
    string Currency,
    decimal Balance) : AdviserBalanceLine(CustomerId, CustomerName, AccountId, AccountName);

public sealed record AdviserHoldingLine(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal CostAmount,
    string CostCurrency,
    decimal MarketValue,
    string MarketCurrency) : AdviserBalanceLine(CustomerId, CustomerName, AccountId, AccountName);

public sealed record AdviserBalanceSubtotal(string Currency, decimal Cash, decimal MarketValue);

public sealed record AdviserBalancesSelection(
    IReadOnlyList<AdviserBalanceLine> Lines,
    IReadOnlyList<AdviserBalanceSubtotal> Subtotals);

public static class AdviserBalancesRows
{
    public static AdviserBalancesSelection Select(
        IReadOnlyList<AdviserBalanceAccount> accounts,
        bool includeClosedAccounts)
    {
        var lines = new List<AdviserBalanceLine>();
        var ordered = accounts
            .Where(account => includeClosedAccounts || !account.Closed)
            .OrderBy(account => account.CustomerName, StringComparer.Ordinal)
            .ThenBy(account => account.CustomerId)
            .ThenBy(account => account.AccountName, StringComparer.Ordinal)
            .ThenBy(account => account.AccountId);
        foreach (var account in ordered)
        {
            if (account.CashBalance != 0m)
            {
                lines.Add(new AdviserCashLine(
                    account.CustomerId,
                    account.CustomerName,
                    account.AccountId,
                    account.AccountName,
                    account.AccountType,
                    account.Currency,
                    account.CashBalance));
            }

            foreach (var holding in account.Holdings
                         .Where(holding => holding.Quantity > 0m)
                         .OrderBy(holding => holding.Symbol, StringComparer.Ordinal)
                         .ThenBy(holding => holding.InstrumentId))
            {
                var (marketValue, marketCurrency) = MarketValue(holding);
                lines.Add(new AdviserHoldingLine(
                    account.CustomerId,
                    account.CustomerName,
                    account.AccountId,
                    account.AccountName,
                    holding.InstrumentId,
                    holding.Symbol,
                    holding.Quantity,
                    holding.CostAmount,
                    holding.CostCurrency,
                    marketValue,
                    marketCurrency));
            }
        }

        return new AdviserBalancesSelection(lines, Subtotals(lines));
    }

    private static IReadOnlyList<AdviserBalanceSubtotal> Subtotals(IReadOnlyList<AdviserBalanceLine> lines)
    {
        var totals = new SortedDictionary<string, CurrencyTotal>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            switch (line)
            {
                case AdviserCashLine cash:
                    Add(totals, cash.Currency).Cash += cash.Balance;
                    break;
                case AdviserHoldingLine holding:
                    Add(totals, holding.MarketCurrency).Market += holding.MarketValue;
                    break;
            }
        }

        return totals
            .Select(pair => new AdviserBalanceSubtotal(pair.Key, pair.Value.Cash, pair.Value.Market))
            .ToList();
    }

    private static CurrencyTotal Add(SortedDictionary<string, CurrencyTotal> totals, string currency)
    {
        if (!totals.TryGetValue(currency, out var total))
        {
            total = new CurrencyTotal();
            totals[currency] = total;
        }

        return total;
    }

    private sealed class CurrencyTotal
    {
        public decimal Cash { get; set; }

        public decimal Market { get; set; }
    }

    private static (decimal Amount, string Currency) MarketValue(AdviserBalanceHolding holding)
    {
        var quote = holding.QuoteCurrency.Trim().ToUpperInvariant();
        var priceCurrency = holding.PriceCurrency?.Trim().ToUpperInvariant();
        if (holding.PriceAmount is > 0m
            && priceCurrency is not null
            && string.Equals(priceCurrency, quote, StringComparison.Ordinal))
        {
            return (holding.Quantity * holding.PriceAmount.Value, priceCurrency);
        }

        return (0m, quote);
    }
}
