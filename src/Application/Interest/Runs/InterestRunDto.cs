namespace MyWealthV2.Application.Interest.Runs;

/// <summary>Interest-run R37: the run item; a list item carries the same.</summary>
public sealed record InterestRunDto(
    Guid Id,
    DateOnly AsOfDate,
    DateOnly PostingDate,
    string Trigger,
    InterestRunPersonDto? RanBy,
    Guid? AccountId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int AccountsProcessed,
    int AccountsFailed,
    int PostingCount,
    IReadOnlyList<InterestRunTotalDto> Totals);

public sealed record InterestRunPersonDto(Guid Id, string Name);

/// <summary>R37: per currency, summed on read from the Transactions whose source is the run.</summary>
public sealed record InterestRunTotalDto(string Currency, int PostingCount, decimal GrossAmount, decimal TaxAmount);

public sealed record CreatedInterestRunDto(Guid Id);
