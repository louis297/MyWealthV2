using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Common.Security;

public sealed class CurrentUserSnapshot
{
    public required User User { get; init; }

    public Tenant? Tenant { get; init; }
}

public static class CurrentUserAccess
{
    public static CurrentUserSnapshot? Resolve(
        User? user,
        Tenant? tenant,
        string? tenantIdClaim,
        string? tenantCodeClaim)
    {
        throw new NotImplementedException();
    }
}
