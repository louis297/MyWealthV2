using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Users;

public sealed class CustomerDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid AdviserId { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Status { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }

    public required DateTimeOffset Created { get; init; }

    public static CustomerDto From(User user, Tenant tenant, User adviser) => new()
    {
        Id = user.PublicId,
        TenantId = tenant.PublicId,
        AdviserId = adviser.PublicId,
        Name = user.Name,
        Email = user.Email,
        Status = ToCamelCase(user.Status.ToString()),
        IsActive = user.IsActive,
        RowVersion = Convert.ToBase64String(user.RowVersion ?? []),
        Created = user.Created
    };

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
