using Microsoft.AspNetCore.Authorization;

namespace MyWealthV2.Web.Infrastructure;

public sealed class AnyPermissionRequirement(params string[] policies) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Policies { get; } = policies;
}
