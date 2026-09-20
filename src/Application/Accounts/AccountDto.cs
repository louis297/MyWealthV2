using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Accounts;

public sealed class AccountDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public required string Currency { get; init; }

    public required string Status { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }

    public static AccountDto From(Account account, Tenant tenant, User customer) => new()
    {
        Id = account.PublicId,
        TenantId = tenant.PublicId,
        CustomerId = customer.PublicId,
        Name = account.Name,
        Type = account.Type.ToString(),
        Currency = account.Currency,
        Status = account.Status.ToString(),
        IsActive = account.IsActive,
        RowVersion = Convert.ToBase64String(account.RowVersion ?? [])
    };
}
