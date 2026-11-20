using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.ClientMoneyAccounts;

/// <summary>Client money account item (docs/features/client-money-accounts.md §8.1).</summary>
public sealed class ClientMoneyAccountDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required string Currency { get; init; }

    public required string BankName { get; init; }

    public required string AccountName { get; init; }

    public required string AccountNumber { get; init; }

    /// <summary>The bank connection its statement is read through (deposit-matching R2).</summary>
    public required string BankConnection { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>How the last read of its statement went; <c>null</c> until the first read was tried (deposit-matching R4).</summary>
    public ClientMoneyAccountFeedDto? BankFeed { get; init; }

    public required string RowVersion { get; init; }

    public required DateTimeOffset Created { get; init; }

    public static ClientMoneyAccountDto From(ClientMoneyAccount row, Tenant tenant) => new()
    {
        Id = row.PublicId,
        TenantId = tenant.PublicId,
        Currency = row.Currency,
        BankName = row.BankName,
        AccountName = row.AccountName,
        AccountNumber = row.AccountNumber,
        BankConnection = row.BankConnection,
        IsActive = row.IsActive,
        RowVersion = Convert.ToBase64String(row.RowVersion ?? []),
        Created = row.Created
    };
}

public sealed record CreatedClientMoneyAccountDto(Guid Id);

/// <summary>Feed status of a client money account (deposit-matching R4). Never carries a message from the party.</summary>
public sealed record ClientMoneyAccountFeedDto(
    DateTimeOffset? LastSucceededAt,
    DateTimeOffset? LastFailedAt,
    string? LastFailure);
