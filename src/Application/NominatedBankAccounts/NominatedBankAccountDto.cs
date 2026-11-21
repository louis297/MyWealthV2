using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.NominatedBankAccounts;

/// <summary>A nominated bank account item (docs/features/withdrawals.md §8.1).</summary>
public sealed class NominatedBankAccountDto
{
    public required Guid Id { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Currency { get; init; }

    public required string BankName { get; init; }

    public required string AccountName { get; init; }

    /// <summary>The full number (R3).</summary>
    public required string AccountNumber { get; init; }

    public required string Status { get; init; }

    public required NominatedBankAccountEventDto Added { get; init; }

    public NominatedBankAccountEventDto? Review { get; init; }

    public NominatedBankAccountEventDto? Removal { get; init; }

    public required string RowVersion { get; init; }
}

/// <summary>Who did something to the row, when, and their note (none on <c>added</c>).</summary>
public sealed class NominatedBankAccountEventDto
{
    public required PersonRefDto By { get; init; }

    public required DateTimeOffset At { get; init; }

    public string? Note { get; init; }
}
