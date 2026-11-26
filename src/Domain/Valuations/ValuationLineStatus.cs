namespace MyWealthV2.Domain.Valuations;

/// <summary>Why a line of a valuation has a value or not (account-valuation R14).</summary>
public enum ValuationLineStatus
{
    Valued = 0,
    NoPrice = 1,
    StalePrice = 2,
    NoRate = 3,
    StaleRate = 4,
    PriceBeforeCorporateAction = 5,
    NegativeQuantity = 6
}
