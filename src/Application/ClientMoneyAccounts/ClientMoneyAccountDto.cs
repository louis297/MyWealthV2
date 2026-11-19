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

    public required bool IsActive { get; init; }

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
        IsActive = row.IsActive,
        RowVersion = Convert.ToBase64String(row.RowVersion ?? []),
        Created = row.Created
    };
}

public sealed record CreatedClientMoneyAccountDto(Guid Id);
