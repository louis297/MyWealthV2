namespace MyWealthV2.Domain.Valuations;

/// <summary>
/// What one Account was worth on one UTC day (docs/features/account-valuation.md). A value worked out by
/// <see cref="Compute"/>, which reads no clock and no store; nothing is stored.
/// </summary>
public sealed record AccountValuation(
    DateOnly Day,
    string Currency,
    decimal Cash,
    IReadOnlyList<ValuationLine> Lines,
    decimal HoldingsValue,
    decimal Total,
    bool Complete,
    int UnvaluedCount)
{
    /// <summary>
    /// Values <paramref name="position"/> in <paramref name="accountCurrency"/>. <paramref name="prices"/> holds the
    /// price row read for each Instrument (none: no row on or before the day); <paramref name="rates"/> holds the
    /// rate row read for each quote currency other than the Account's (none: no row of the pair);
    /// <paramref name="decimalPlaces"/> gives the decimal places of each currency.
    /// </summary>
    public static AccountValuation Compute(
        AccountPosition position,
        string accountCurrency,
        IReadOnlyDictionary<string, int> decimalPlaces,
        IReadOnlyDictionary<int, StoredPrice> prices,
        IReadOnlyDictionary<string, StoredRate> rates,
        IReadOnlyList<BasisChange> basisChanges)
    {
        var currency = Code(accountCurrency);
        var lines = position.Lines
            .Select(line => Value(line, currency, decimalPlaces, prices, rates))
            .ToList();

        // R16: the totals add the Valued lines only; a line that is not valued is never counted as 0.
        var holdingsValue = lines.Where(line => line.Status == ValuationLineStatus.Valued).Sum(line => line.Value!.Value);
        var unvalued = lines.Count(line => line.Status != ValuationLineStatus.Valued);
        return new AccountValuation(
            position.Day, currency, position.Cash, lines, holdingsValue, position.Cash + holdingsValue, unvalued == 0, unvalued);
    }

    private static ValuationLine Value(
        PositionLine line,
        string accountCurrency,
        IReadOnlyDictionary<string, int> decimalPlaces,
        IReadOnlyDictionary<int, StoredPrice> prices,
        IReadOnlyDictionary<string, StoredRate> rates)
    {
        var quantity = line.LedgerQuantity;
        var price = prices[line.InstrumentId];
        var quote = Code(price.Currency);

        // R15: the market value is rounded to the quote currency; the value is worked out from the rounded figure.
        var marketValue = Round(quantity * price.Amount, Places(decimalPlaces, quote));
        if (quote == accountCurrency)
        {
            return new ValuationLine(
                line.InstrumentId, quantity, line.LedgerQuantity, ValuationLineStatus.Valued, price, marketValue, null, marketValue);
        }

        var rate = rates[quote];
        var value = Round(marketValue * rate.Rate, Places(decimalPlaces, accountCurrency));
        return new ValuationLine(
            line.InstrumentId, quantity, line.LedgerQuantity, ValuationLineStatus.Valued, price, marketValue, rate, value);
    }

    private static decimal Round(decimal amount, int places) => decimal.Round(amount, places, MidpointRounding.AwayFromZero);

    private static int Places(IReadOnlyDictionary<string, int> decimalPlaces, string currency) =>
        decimalPlaces.TryGetValue(currency, out var places)
            ? places
            : throw new DomainException($"Currency {currency} has no decimal places.");

    private static string Code(string currency) => currency.Trim().ToUpperInvariant();
}

/// <summary>
/// One Instrument of a valuation (account-valuation R14, R15, R21). Only a <see cref="ValuationLineStatus.Valued"/>
/// line has a <see cref="Value"/>. <see cref="Quantity"/> differs from <see cref="LedgerQuantity"/> only on a line
/// R18 restated.
/// </summary>
public sealed record ValuationLine(
    int InstrumentId,
    decimal Quantity,
    decimal LedgerQuantity,
    ValuationLineStatus Status,
    StoredPrice? Price,
    decimal? MarketValue,
    StoredRate? Fx,
    decimal? Value);
