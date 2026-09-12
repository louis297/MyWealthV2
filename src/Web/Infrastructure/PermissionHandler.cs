using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace MyWealthV2.Web.Infrastructure;

public sealed class PermissionHandler(ICurrentUser currentUser) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (currentUser.Role is { } role && RolePermissions.Has(role, requirement.Policy))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
