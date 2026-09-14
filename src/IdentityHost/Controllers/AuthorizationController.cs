using System.Security.Claims;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MyWealthV2.IdentityHost.Controllers;

public class AuthorizationController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IApplicationDbContext db) : Controller
{
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
                },
                IdentityConstants.ApplicationScheme);
        }

        var identityUser = await userManager.GetUserAsync(User);
        if (identityUser is null)
        {
            await signInManager.SignOutAsync();
            return Challenge(IdentityConstants.ApplicationScheme);
        }

        var person = await db.Users.SingleOrDefaultAsync(
            row => row.IdentityUserId == identityUser.Id,
            cancellationToken);

        if (person is null)
        {
            await signInManager.SignOutAsync();
            return Challenge(IdentityConstants.ApplicationScheme);
        }

        Tenant? tenant = null;
        if (person.TenantId is not null)
        {
            tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Id == person.TenantId, cancellationToken);
        }

        if (!LoginEligibility.CanComplete(person, tenant))
        {
            await signInManager.SignOutAsync();
            return Challenge(IdentityConstants.ApplicationScheme);
        }

        if (!ClientRoleAllowList.Allows(request.ClientId, person.Role))
        {
            await signInManager.SignOutAsync();
            return Challenge(IdentityConstants.ApplicationScheme);
        }

        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, person.PublicId.ToString());
        identity.SetClaim(Claims.Name, person.Name);
        identity.SetClaim(Claims.Email, person.Email);
        identity.SetClaim(Claims.Role, ToCamelCase(person.Role.ToString()));

        if (tenant is not null)
        {
            identity.SetClaim("tenant_id", tenant.PublicId.ToString());
            identity.SetClaim("tenant_code", tenant.Code);
        }
        else
        {
            identity.SetClaim("tenant_id", string.Empty);
            identity.SetClaim("tenant_code", string.Empty);
        }

        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Name or Claims.Email or Claims.Role or "tenant_id" or "tenant_code"
                => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken]
        });

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
