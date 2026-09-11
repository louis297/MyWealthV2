using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.IdentityHost.Pages;

public class LoginModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IApplicationDbContext db) : PageModel
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
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null || !LoginEligibility.CanComplete(user, await ResolveTenantAsync(user, cancellationToken)))
        {
            Error = UniformFailure;
            return Page();
        }

        var identityUser = await userManager.FindByIdAsync(user.IdentityUserId);
        if (identityUser is null || !await userManager.CheckPasswordAsync(identityUser, Password))
        {
            Error = UniformFailure;
            return Page();
        }

        await signInManager.SignInAsync(identityUser, isPersistent: false);

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

        return await db.Users.SingleOrDefaultAsync(
            person => person.TenantId == tenant.Id && person.Email == Email,
            cancellationToken);
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
