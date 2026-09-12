using Microsoft.AspNetCore.Authorization;

namespace MyWealthV2.Web.Infrastructure;

public sealed class PermissionRequirement(string policy) : IAuthorizationRequirement
{
    public string Policy { get; } = policy;
}
