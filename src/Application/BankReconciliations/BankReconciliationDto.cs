using MyWealthV2.Application.ClientMoneyAccounts;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>The client money account of a run or an overview entry (bank-reconciliation R19).</summary>
public sealed record BankReconciliationAccountDto(
    Guid Id,
    string Currency,
    string BankName,
    string AccountName,
    string AccountNumber,
    bool IsActive);

public sealed record BankReconciliationPersonDto(Guid Id, string Name);

public sealed record BankReconciliationTotalDto(int Count, decimal Amount);

public sealed record BankReconciliationTotalsDto(
    BankReconciliationTotalDto Breaks,
    BankReconciliationTotalDto Timing,
    BankReconciliationTotalDto Known);

public sealed record BankReconciliationPaymentsDto(int Count, decimal Amount);

/// <summary>A run without its items, feed and payments in flight (R20): a list item and the overview's latest.</summary>
public class BankReconciliationListItemDto
{
    public required Guid Id { get; init; }

    public required BankReconciliationAccountDto ClientMoneyAccount { get; init; }

    public required string Trigger { get; init; }

    public required DateTimeOffset RanAt { get; init; }

    public required BankReconciliationPersonDto? RanBy { get; init; }

    public required string Status { get; init; }

    public required decimal? BankBalance { get; init; }

    public required DateTimeOffset? BalanceAt { get; init; }

    public required decimal? OpeningBalance { get; init; }

    public required decimal? StatementLinesTotal { get; init; }

    public required decimal CustodialCash { get; init; }

    public required decimal? Difference { get; init; }

    public required BankReconciliationTotalsDto Totals { get; init; }

    public required decimal ReservedCash { get; init; }
}

/// <summary>A run with its items (R21, §8.1).</summary>
public sealed class BankReconciliationDto : BankReconciliationListItemDto
{
    public required BankReconciliationPaymentsDto PaymentsInFlight { get; init; }

    /// <summary>Trade-settlement R25: the open trade settlements of the account, recorded without an item.</summary>
    public required BankReconciliationPaymentsDto OpenSettlements { get; init; }

    public required ClientMoneyAccountFeedDto Feed { get; init; }

    public required IReadOnlyList<BankReconciliationItemDto> Items { get; init; }
}

/// <summary>A line as it was at the run (R21).</summary>
public sealed record BankReconciliationLineDto(
    Guid Id,
    string Direction,
    string ValueDate,
    string Status,
    string? UnmatchedReason,
    string? Note);

public sealed record BankReconciliationItemDto(
    string Kind,
    string Class,
    decimal Amount,
    int Count,
    BankReconciliationLineDto? StatementLine,
    Guid? ClientMoneyAccountId);

/// <summary>One entry of the overview (R19).</summary>
public sealed record BankReconciliationOverviewDto(
    BankReconciliationAccountDto ClientMoneyAccount,
    ClientMoneyAccountFeedDto? BankFeed,
    BankReconciliationListItemDto? Latest);
