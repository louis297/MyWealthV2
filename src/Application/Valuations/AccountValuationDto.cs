namespace MyWealthV2.Application.Valuations;

/// <summary>The item of <c>GET /accounts/{id}/valuation</c> (docs/features/account-valuation.md R21).</summary>
public sealed record AccountValuationDto(
    Guid AccountId,
    DateOnly Date,
    string Currency,
    decimal Cash,
    IReadOnlyList<AccountValuationHoldingDto> Holdings,
    decimal HoldingsValue,
    decimal Total,
    bool Complete,
    int UnvaluedCount);

/// <summary>One line of a valuation. Only a <c>Valued</c> line has a <c>value</c>.</summary>
public sealed record AccountValuationHoldingDto(
    Guid InstrumentId,
    string Symbol,
    string Name,
    decimal Quantity,
    decimal LedgerQuantity,
    string Status,
    AccountValuationPriceDto? Price,
    decimal? MarketValue,
    AccountValuationFxDto? Fx,
    decimal? Value);

/// <summary>The stored price row a line read, in the Instrument's quote currency.</summary>
public sealed record AccountValuationPriceDto(decimal Amount, string Currency, DateOnly Date, string Source);

/// <summary>The stored rate row a line read: Account currency per 1 unit of the quote currency.</summary>
public sealed record AccountValuationFxDto(decimal Rate, DateOnly Date, string Source);
