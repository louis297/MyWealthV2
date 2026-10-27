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

        if (!ClientCookieSchemes.TryGet(request.ClientId, out var scheme))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
        };

        var authenticated = await HttpContext.AuthenticateAsync(scheme);
        var session = authenticated.Principal;
        if (authenticated.Succeeded != true || session is null)
        {
            return Challenge(properties, scheme);
        }

        var identityUser = await userManager.GetUserAsync(session);
        if (identityUser is null)
        {
            await HttpContext.SignOutAsync(scheme);
            return Challenge(scheme);
        }

        var person = await db.Users.SingleOrDefaultAsync(
            row => row.IdentityUserId == identityUser.Id,
            cancellationToken);

        if (person is null)
        {
            await HttpContext.SignOutAsync(scheme);
            return Challenge(scheme);
        }

        Tenant? tenant = null;
        if (person.TenantId is not null)
        {
            tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Id == person.TenantId, cancellationToken);
        }

        if (!LoginEligibility.CanComplete(person, tenant))
        {
            await HttpContext.SignOutAsync(scheme);
            return Challenge(scheme);
        }

        if (!ClientRoleAllowList.Allows(request.ClientId, person.Role))
        {
            await HttpContext.SignOutAsync(scheme);
            return Challenge(scheme);
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

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (ClientCookieSchemes.TryGet(request?.ClientId, out var scheme))
        {
            await HttpContext.SignOutAsync(scheme);
        }

        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
