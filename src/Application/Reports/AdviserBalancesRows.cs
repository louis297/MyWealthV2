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
        foreach (var account in accounts)
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

            foreach (var holding in account.Holdings)
            {
                if (holding.Quantity <= 0m)
                {
                    continue;
                }

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

        return new AdviserBalancesSelection(lines, []);
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
