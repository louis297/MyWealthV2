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
        return new CurrentUserDto
        {
            Id = user.PublicId,
            Name = user.Name,
            Email = user.Email,
            Role = ToCamelCase(user.Role.ToString()),
            Status = ToCamelCase(user.Status.ToString()),
            TenantId = tenant?.PublicId,
            TenantCode = tenant?.Code,
            AdviserId = adviser?.PublicId,
            RowVersion = Convert.ToBase64String(user.RowVersion ?? [])
        };
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
