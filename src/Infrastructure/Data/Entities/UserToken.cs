namespace MyWealthV2.Infrastructure.Data.Entities;

public class UserToken
{
    public int Id { get; set; }

    public int Purpose { get; set; }

    public int? TenantId { get; set; }

    public required string Email { get; set; }

    public string? IdentityUserId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset Created { get; set; }

    public required string CreatedBy { get; set; }
}
