using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.MigrationBatches;

/// <summary>Migration batches R28, R29: a batch on the list, and with the item fields when read by id.</summary>
public sealed class MigrationBatchDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string PreviousProvider { get; init; }

    public required string AsOfDate { get; init; }

    public required string Status { get; init; }

    public required int EntryCount { get; init; }

    public required int HoldingCount { get; init; }

    public required PersonRefDto? CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required PersonRefDto? SubmittedBy { get; init; }

    public required DateTimeOffset? SubmittedAt { get; init; }

    public required PersonRefDto? PostedBy { get; init; }

    public required DateTimeOffset? PostedAt { get; init; }
}

/// <summary>Migration batches R29: the item of one batch.</summary>
public sealed class MigrationBatchItemDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string PreviousProvider { get; init; }

    public required string AsOfDate { get; init; }

    public required string Status { get; init; }

    public required int EntryCount { get; init; }

    public required int HoldingCount { get; init; }

    public required PersonRefDto? CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required PersonRefDto? SubmittedBy { get; init; }

    public required DateTimeOffset? SubmittedAt { get; init; }

    public required PersonRefDto? PostedBy { get; init; }

    public required DateTimeOffset? PostedAt { get; init; }

    public required MigrationTotalsDto ControlTotals { get; init; }

    public required MigrationTotalsDto ComputedTotals { get; init; }

    public required IReadOnlyList<MigrationMoneyDto> FundingTotals { get; init; }

    public required IReadOnlyList<MigrationFundingLineDto> Funding { get; init; }

    public required IReadOnlyList<MigrationEntryDto> Entries { get; init; }

    public required MigrationValidationDto? Validation { get; init; }

    public required MigrationActionDto? Returned { get; init; }

    public required MigrationActionDto? Cancelled { get; init; }

    public required string RowVersion { get; init; }
}

/// <summary>R5, R8: the four totals of a batch.</summary>
public sealed record MigrationTotalsDto(
    int AccountCount,
    IReadOnlyList<MigrationMoneyDto> Cash,
    IReadOnlyList<MigrationQuantityDto> Quantity,
    IReadOnlyList<MigrationMoneyDto> Cost);

public sealed record MigrationMoneyDto(string Currency, decimal Amount);

public sealed record MigrationQuantityDto(Guid InstrumentId, decimal Quantity);

/// <summary>R12, §8.1: a statement line that funds the batch.</summary>
public sealed record MigrationFundingLineDto(
    Guid StatementLineId,
    string Currency,
    decimal Amount,
    string ValueDate,
    string? PayerName,
    string? Reference);

/// <summary>R29: one entry with its Account and Instruments expanded.</summary>
public sealed record MigrationEntryDto(
    MigrationEntryAccountDto Account,
    string? ExternalReference,
    decimal? Cash,
    IReadOnlyList<MigrationHoldingDto> Holdings,
    Guid? OpeningTransactionId,
    Guid? OpeningHoldingsTransactionId);

public sealed record MigrationEntryAccountDto(
    Guid Id,
    string Name,
    string? DepositReference,
    string Currency,
    PersonRefDto Customer);

public sealed record MigrationHoldingDto(MigrationInstrumentDto Instrument, decimal Quantity, decimal Cost);

public sealed record MigrationInstrumentDto(Guid Id, string Symbol, string Name, string QuoteCurrency);

/// <summary>R9, R29: whether the batch is valid now, and why not.</summary>
public sealed record MigrationValidationDto(bool IsValid, IReadOnlyList<MigrationIssueDto> Issues);

public sealed record MigrationIssueDto(
    string Code,
    Guid? AccountId,
    Guid? InstrumentId,
    string? Currency,
    string? Total,
    string Message);

/// <summary>R18, R19: the person, the time and the note of a return or a cancel.</summary>
public sealed record MigrationActionDto(PersonRefDto? By, DateTimeOffset At, string Note);
