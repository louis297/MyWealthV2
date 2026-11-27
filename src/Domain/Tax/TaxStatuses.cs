namespace MyWealthV2.Domain.Tax;

/// <summary>Where the NZD amount of a tax event comes from, or why it has none (tax-year-events R31).</summary>
public enum TaxAmountStatus
{
    /// <summary>NZD legs, used as booked (R18).</summary>
    Stated = 0,

    /// <summary>Legs in another currency, converted at the stored rate of the day (R18).</summary>
    Converted = 1,

    /// <summary>The market value of the day of an arrival or a departure (R14).</summary>
    MarketValue = 2,

    /// <summary>The kind has no amount: a reorganisation, a move or a correction.</summary>
    None = 3,

    NoRate = 4,
    StaleRate = 5,
    NoPrice = 6,
    StalePrice = 7
}

/// <summary>Whether the opening or the closing of a tax holding has a value (tax-year-events R22, R23).</summary>
public enum TaxValueStatus
{
    Valued = 0,
    NotValued = 1,

    /// <summary>The treatment needs no value: no price and no rate is read (R23).</summary>
    NotRequired = 2
}

/// <summary>Why a tax holding is incomplete (tax-year-events R27).</summary>
public enum TaxIncompleteReason
{
    NotClassified = 0,
    OpeningNotValued = 1,
    ClosingNotValued = 2,
    AmountMissing = 3,
    NegativeQuantity = 4,
    NoComparableQuantity = 5
}

/// <summary>A figure that is there and rests on an assumption or on somebody else's word (tax-year-events R28).</summary>
public enum TaxNotice
{
    ExternalFigures = 0,
    ArrivalAtMarketValue = 1,
    DepartureAtMarketValue = 2,
    Move = 3,
    Scrip = 4,
    Correction = 5,
    DividendTaxSourceDisagrees = 6
}
