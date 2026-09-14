using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace MyWealthV2.Web.Infrastructure;

public sealed class AnyPermissionHandler(ICurrentUser currentUser) : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        if (currentUser.Role is { } role
            && requirement.Policies.Any(policy => RolePermissions.Has(role, policy)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
