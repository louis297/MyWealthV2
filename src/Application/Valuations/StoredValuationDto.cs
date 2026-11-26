namespace MyWealthV2.Application.Valuations;

/// <summary>Valuation-run R17: one day of the series of an account's stored valuations.</summary>
public sealed record StoredValuationSummaryDto(
    DateOnly Date,
    string Currency,
    decimal Cash,
    decimal HoldingsValue,
    decimal Total,
    bool Complete,
    int UnvaluedCount);

/// <summary>Valuation-run R18: the stored valuation of one day, as the item of account-valuation R21 with the run that
/// stored it and when.</summary>
public sealed record StoredValuationDto(
    Guid AccountId,
    DateOnly Date,
    string Currency,
    decimal Cash,
    IReadOnlyList<AccountValuationHoldingDto> Holdings,
    decimal HoldingsValue,
    decimal Total,
    bool Complete,
    int UnvaluedCount,
    Guid RunId,
    DateTimeOffset ValuedAt);
