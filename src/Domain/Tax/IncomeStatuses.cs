namespace MyWealthV2.Domain.Tax;

/// <summary>The group of a dividend in the income summary, by the FIF treatment of its Instrument for the tax year
/// (docs/features/income-summary.md R6). The four groups are always given, in this order.</summary>
public enum IncomeGroup
{
    /// <summary><c>NotForeign</c>: New Zealand companies.</summary>
    NewZealand = 0,

    /// <summary><c>ExemptAsxListed</c> and <c>ExemptOther</c>: foreign companies outside the FIF rules.</summary>
    ForeignExempt = 1,

    /// <summary><c>Attributing</c> and <c>AttributingNoFdr</c>: attributing interests.</summary>
    Attributing = 2,

    /// <summary>No treatment for the tax year.</summary>
    NotClassified = 3
}

/// <summary>Why the income summary is incomplete (R11).</summary>
public enum IncomeIncompleteReason
{
    /// <summary>A dividend of the tax holding, or an interest item of the Account, has a missing figure (R10).</summary>
    AmountMissing = 0,

    /// <summary>A tax holding that is not classified has a dividend in the year.</summary>
    NotClassified = 1
}

/// <summary>What the figures of the income summary rest on (R12). A notice does not make it incomplete.</summary>
public enum IncomeNotice
{
    ExternalFigures = 0,
    DividendTaxSourceDisagrees = 1,
    Scrip = 2,
    InterestCharged = 3
}
