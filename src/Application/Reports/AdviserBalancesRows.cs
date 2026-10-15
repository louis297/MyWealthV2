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
        }

        return new AdviserBalancesSelection(lines, []);
    }
}
