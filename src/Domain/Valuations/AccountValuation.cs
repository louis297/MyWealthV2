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
        IReadOnlyList<BasisChange> basisChanges) =>
        new(position.Day, accountCurrency, position.Cash, [], 0m, 0m, false, 0);
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
