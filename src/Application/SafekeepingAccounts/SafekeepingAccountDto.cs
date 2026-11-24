using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.SafekeepingAccounts;

/// <summary>Safekeeping account item (docs/features/in-kind-transfers.md R7).</summary>
public sealed class SafekeepingAccountDto
{
    public required Guid Id { get; init; }

    public required string Currency { get; init; }

    public required string CustodianName { get; init; }

    public required string AccountName { get; init; }

    public required string AccountNumber { get; init; }

    public required string CustodianConnection { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>How the last read of its feed went; <c>null</c> until the first read was tried (R7).</summary>
    public CustodianFeedDto? CustodianFeed { get; init; }

    public required string RowVersion { get; init; }

    public static SafekeepingAccountDto From(SafekeepingAccount row, CustodianFeedCursor? cursor) => new()
    {
        Id = row.PublicId,
        Currency = row.Currency,
        CustodianName = row.CustodianName,
        AccountName = row.AccountName,
        AccountNumber = row.AccountNumber,
        CustodianConnection = row.CustodianConnection,
        IsActive = row.IsActive,
        CustodianFeed = CustodianFeedDto.From(cursor),
        RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
    };
}

public sealed record CreatedSafekeepingAccountDto(Guid Id);

/// <summary>Feed status of a safekeeping account (R7). Never carries a message from the party.</summary>
public sealed record CustodianFeedDto(
    DateTimeOffset? LastSucceededAt,
    DateTimeOffset? LastFailedAt,
    string? LastFailure,
    DateTimeOffset? PositionsAt)
{
    public static CustodianFeedDto? From(CustodianFeedCursor? cursor) =>
        cursor is null
            ? null
            : new CustodianFeedDto(cursor.LastSucceededAt, cursor.LastFailedAt, cursor.LastFailure?.ToString(), cursor.PositionsAt);
}
