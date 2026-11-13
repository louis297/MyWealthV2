using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.IdentityHost;

/// <summary>
/// Rejects an identity cookie whose security stamp no longer matches the identity user
/// (identity-auth amendment 2026-11-13, security stamp). Runs on every request; no interval.
/// </summary>
public static class IdentityCookieSecurityStamp
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (await MatchesAsync(context))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }

    private static async Task<bool> MatchesAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { } principal)
        {
            return false;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var cookieStamp = principal.FindFirstValue(userManager.Options.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrEmpty(cookieStamp))
        {
            return false;
        }

        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }

        var currentStamp = await userManager.GetSecurityStampAsync(user);
        return string.Equals(cookieStamp, currentStamp, StringComparison.Ordinal);
    }
}
