using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Users;

internal static class CustomerScope
{
    public static bool ManagesTenant(ICurrentUser currentUser) =>
        currentUser.Role is { } role && RolePermissions.Has(role, Policies.CustomersManage);

    public static int? AssignedAdviserInternalId(ICurrentUser currentUser) =>
        ManagesTenant(currentUser) ? null : currentUser.Person?.Id;
}
