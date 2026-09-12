using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

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
        if (user is null)
        {
            return null;
        }

        var claimTenantId = EmptyToNull(tenantIdClaim);
        var claimTenantCode = EmptyToNull(tenantCodeClaim);

        if (user.Role == UserRole.SystemAdmin)
        {
            if (user.TenantId is not null || tenant is not null || claimTenantId is not null || claimTenantCode is not null)
            {
                return null;
            }

            return new CurrentUserSnapshot { User = user, Tenant = null };
        }

        if (tenant is null || user.TenantId != tenant.Id)
        {
            return null;
        }

        if (!Guid.TryParse(claimTenantId, out var claimedPublicId) || claimedPublicId != tenant.PublicId)
        {
            return null;
        }

        if (!string.Equals(claimTenantCode, tenant.Code, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new CurrentUserSnapshot { User = user, Tenant = tenant };
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
