using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.IdentityHost.Pages;

public class LoginModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    DummyPasswordHash dummyPasswordHash,
    IApplicationDbContext db,
    ITenantIdAccessor tenantIds) : PageModel
{
    public const string UniformFailure = "Invalid login.";

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string? TenantCode { get; set; }

    public string? Error { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        // Every POST verifies exactly one password hash, so the response time does not tell
        // who exists or who is eligible (identity-auth amendment 2026-11-13, lockout).
        var password = Password ?? string.Empty;
        var user = await ResolveUserAsync(cancellationToken);
        var identityUser = user is null ? null : await userManager.FindByIdAsync(user.IdentityUserId);
        if (user is null || identityUser is null)
        {
            passwordHasher.VerifyHashedPassword(new ApplicationUser(), dummyPasswordHash.Value, password);
            Error = UniformFailure;
            return Page();
        }

        // Not SignInManager.CheckPasswordSignInAsync: it skips the hash for a locked user.
        var lockedOut = await userManager.IsLockedOutAsync(identityUser);
        var passwordMatches = await userManager.CheckPasswordAsync(identityUser, password);
        if (lockedOut)
        {
            Error = UniformFailure;
            return Page();
        }

        if (!passwordMatches)
        {
            await userManager.AccessFailedAsync(identityUser);
            Error = UniformFailure;
            return Page();
        }

        await userManager.ResetAccessFailedCountAsync(identityUser);

        if (!LoginEligibility.CanComplete(user, await ResolveTenantAsync(user, cancellationToken)))
        {
            Error = UniformFailure;
            return Page();
        }

        var clientId = ClientIdFromReturnUrl(returnUrl);
        if (!ClientCookieSchemes.TryGet(clientId, out var scheme)
            || !ClientRoleAllowList.Allows(clientId, user.Role))
        {
            Error = UniformFailure;
            return Page();
        }

        var principal = await signInManager.CreateUserPrincipalAsync(identityUser);
        await HttpContext.SignInAsync(
            scheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return Redirect("~/");
    }

    private async Task<User?> ResolveUserAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(TenantCode))
        {
            return await db.Users.SingleOrDefaultAsync(
                person => person.Role == UserRole.SystemAdmin && person.Email == Email,
                cancellationToken);
        }

        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == TenantCode,
            cancellationToken);

        if (tenant is null)
        {
            return null;
        }

        tenantIds.Engage(tenant.Id);

        return await db.Users.SingleOrDefaultAsync(
            person => person.TenantId == tenant.Id && person.Email == Email,
            cancellationToken);
    }

    private static string? ClientIdFromReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        var queryIndex = returnUrl.IndexOf('?');
        if (queryIndex < 0 || queryIndex == returnUrl.Length - 1)
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(returnUrl[queryIndex..]);
        return query.TryGetValue("client_id", out var values) ? values.ToString() : null;
    }

    private async Task<Tenant?> ResolveTenantAsync(User user, CancellationToken cancellationToken)
    {
        if (user.TenantId is null)
        {
            return null;
        }

        return await db.Tenants.SingleOrDefaultAsync(row => row.Id == user.TenantId, cancellationToken);
    }
}
