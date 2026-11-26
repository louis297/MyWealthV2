namespace MyWealthV2.Domain.Valuations;

/// <summary>
/// One entitlement posting of a <c>Manual</c> corporate action on the Account (account-valuation R18): a <c>Split</c>
/// or <c>Bonus</c> Transaction, not reversed, with the ex-date and ratio of its corporate action detail.
/// </summary>
public sealed record BasisChange(
    int InstrumentId,
    DateOnly ExDate,
    DateTimeOffset BookedAt,
    CorporateActionKind Kind,
    int RatioNew,
    int RatioHeld);
