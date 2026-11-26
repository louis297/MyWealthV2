using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Reports;

/// <summary>One account of the Adviser Balances Report for the end of a past day (valuation-history R17): its cash and
/// valuation lines of that day, or <see cref="NotValuedYet"/> for a Custodial account with no stored valuation.</summary>
public sealed record AdviserPastAccount(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string AccountType,
    string CustodyMode,
    string Currency,
    bool NotValuedYet,
    decimal Cash,
    IReadOnlyList<AdviserPastHolding> Holdings);

/// <summary>One valuation line of a past day: the quantity (restated or not), the price row and its date, and the
/// market value in the quote currency when the line has one.</summary>
public sealed record AdviserPastHolding(
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal LedgerQuantity,
    ValuationLineStatus Status,
    decimal? PriceAmount,
    DateOnly? PriceDate,
    decimal? MarketValue,
    string QuoteCurrency);

/// <summary>A holding line of a past day (R18). <see cref="MarketValue"/> is null when the line was not valued.</summary>
public sealed record AdviserPastHoldingLine(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string CustodyMode,
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal LedgerQuantity,
    ValuationLineStatus Status,
    decimal? PriceAmount,
    DateOnly? PriceDate,
    decimal? MarketValue,
    string MarketCurrency) : AdviserBalanceLine(CustomerId, CustomerName, AccountId, AccountName, CustodyMode);

/// <summary>A Custodial account expected on the day with no stored valuation of it (R17): "Not valued yet".</summary>
public sealed record AdviserNotValuedLine(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string AccountType,
    string CustodyMode) : AdviserBalanceLine(CustomerId, CustomerName, AccountId, AccountName, CustodyMode);

public sealed record AdviserBalancesPastSelection(
    IReadOnlyList<AdviserBalanceLine> Lines,
    IReadOnlyList<AdviserBalanceSubtotal> Subtotals,
    IReadOnlyList<AdviserBalanceModeSubtotals> ModeSubtotals,
    int HoldingsNotValued,
    int AccountsNotValuedYet);

/// <summary>Valuation-history R17–R19: the lines and subtotals of the Adviser Balances Report for a past date.</summary>
public static class AdviserBalancesPastRows
{
    public static AdviserBalancesPastSelection Select(IReadOnlyList<AdviserPastAccount> accounts)
    {
        var lines = new List<AdviserBalanceLine>();
        var ordered = accounts
            .OrderBy(account => account.CustomerName, StringComparer.Ordinal)
            .ThenBy(account => account.CustomerId)
            .ThenBy(account => account.AccountName, StringComparer.Ordinal)
            .ThenBy(account => account.AccountId);
        foreach (var account in ordered)
        {
            if (account.NotValuedYet)
            {
                lines.Add(new AdviserNotValuedLine(
                    account.CustomerId, account.CustomerName, account.AccountId, account.AccountName, account.AccountType,
                    account.CustodyMode));
                continue;
            }

            // R17: an account with no cash and no holding on the day is left out.
            if (account.Cash != 0m)
            {
                lines.Add(new AdviserCashLine(
                    account.CustomerId, account.CustomerName, account.AccountId, account.AccountName, account.AccountType,
                    account.CustodyMode, account.Currency, account.Cash));
            }

            foreach (var holding in account.Holdings
                         .OrderBy(holding => holding.Symbol, StringComparer.Ordinal)
                         .ThenBy(holding => holding.InstrumentId))
            {
                lines.Add(new AdviserPastHoldingLine(
                    account.CustomerId, account.CustomerName, account.AccountId, account.AccountName, account.CustodyMode,
                    holding.InstrumentId, holding.Symbol, holding.Quantity, holding.LedgerQuantity, holding.Status,
                    holding.PriceAmount, holding.PriceDate, holding.MarketValue, holding.QuoteCurrency.Trim().ToUpperInvariant()));
            }
        }

        return new AdviserBalancesPastSelection(
            lines,
            Subtotals(lines),
            lines.GroupBy(line => line.CustodyMode)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new AdviserBalanceModeSubtotals(group.Key, Subtotals(group.ToList())))
                .ToList(),
            lines.OfType<AdviserPastHoldingLine>().Count(line => line.MarketValue is null),
            lines.OfType<AdviserNotValuedLine>().Count());
    }

    /// <summary>R18: the label of a line that is not valued; none for a valued line or one with no rate.</summary>
    public static string? StatusLabel(ValuationLineStatus status) => status switch
    {
        ValuationLineStatus.NoPrice => "No price",
        ValuationLineStatus.StalePrice => "Price too old",
        ValuationLineStatus.PriceBeforeCorporateAction => "Price from before a corporate action",
        ValuationLineStatus.NegativeQuantity => "Quantity below 0",
        _ => null
    };

    /// <summary>R19: per currency, the cash and the market value of the lines that have one. A line with no market
    /// value (and an account not valued yet) is left out; a line with no rate counts in its quote currency.</summary>
    private static IReadOnlyList<AdviserBalanceSubtotal> Subtotals(IReadOnlyList<AdviserBalanceLine> lines)
    {
        var totals = new SortedDictionary<string, (decimal Cash, decimal Market)>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            switch (line)
            {
                case AdviserCashLine cash:
                    var cashTotal = totals.GetValueOrDefault(cash.Currency);
                    totals[cash.Currency] = (cashTotal.Cash + cash.Balance, cashTotal.Market);
                    break;
                case AdviserPastHoldingLine { MarketValue: { } value } holding:
                    var marketTotal = totals.GetValueOrDefault(holding.MarketCurrency);
                    totals[holding.MarketCurrency] = (marketTotal.Cash, marketTotal.Market + value);
                    break;
            }
        }

        return totals.Select(pair => new AdviserBalanceSubtotal(pair.Key, pair.Value.Cash, pair.Value.Market)).ToList();
    }
}
