using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Users;

public sealed class CurrentUserDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Role { get; init; }

    public required string Status { get; init; }

    public Guid? TenantId { get; init; }

    public string? TenantCode { get; init; }

    public Guid? AdviserId { get; init; }

    public required string RowVersion { get; init; }

    public static CurrentUserDto From(User user, Tenant? tenant, User? adviser)
    {
        throw new NotImplementedException();
    }
}
