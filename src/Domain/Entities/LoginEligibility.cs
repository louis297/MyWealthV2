using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Domain.Entities;

public static class LoginEligibility
{
    public static bool CanComplete(User user, Tenant? tenant)
    {
        if (user.Status != UserStatus.Active)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(user.IdentityUserId))
        {
            return false;
        }

        if (user.Role == UserRole.SystemAdmin)
        {
            return tenant is null && user.TenantId is null;
        }

        if (tenant is null || !tenant.IsEnabled)
        {
            return false;
        }

        return user.TenantId == tenant.Id;
    }
}
