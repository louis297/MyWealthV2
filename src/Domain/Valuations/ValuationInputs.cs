namespace MyWealthV2.Domain.Valuations;

/// <summary>The stored price a valuation reads (account-valuation R9): the unadjusted close in the Instrument's quote
/// currency, with the date and source of its row.</summary>
public sealed record StoredPrice(decimal Amount, string Currency, DateOnly AsOfDate, string Source);

/// <summary>The stored rate a valuation reads (account-valuation R10): Account currency per 1 unit of the quote
/// currency, with the date and source of its row.</summary>
public sealed record StoredRate(decimal Rate, DateOnly AsOfDate, string Source);
