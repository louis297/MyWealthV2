namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>Valuation-run R22: the run item; a list item carries the same.</summary>
public sealed record ValuationRunDto(
    Guid Id,
    DateOnly ThroughDate,
    string Trigger,
    ValuationRunPersonDto? RanBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int AccountsProcessed,
    int AccountsFailed,
    int DaysValued,
    int DaysIncomplete);

public sealed record ValuationRunPersonDto(Guid Id, string Name);

public sealed record CreatedValuationRunDto(Guid Id);
