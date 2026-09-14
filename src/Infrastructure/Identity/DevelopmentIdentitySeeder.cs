using System.Reflection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Infrastructure.Identity;

public static class DevelopmentIdentitySeeder
{
    public const string SystemAdminEmail = "system-admin@localhost";
    public const string SystemAdminPassword = "Administrator1!";

    public const string DemoTenantCode = "demo";
    public const string DemoTenantName = "Demo Advisory";

    public const string TenantAdminEmail = "tenant-admin@localhost";
    public const string TenantAdminPassword = SystemAdminPassword;

    public const string AdviserEmail = "adviser@localhost";
    public const string AdviserPassword = SystemAdminPassword;

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await EnsurePersonAsync(
            db,
            userManager,
            UserRole.SystemAdmin,
            name: "System Admin",
            email: SystemAdminEmail,
            password: SystemAdminPassword,
            tenantId: null,
            cancellationToken);

        // Functional tests reseed this method after Respawn and assert exact tenant/person list counts.
        if (IsTestHost())
        {
            return;
        }

        var catalog = services.GetRequiredService<ICurrencyCatalog>();
        var nzd = catalog.TryGet("NZD")
                  ?? throw new InvalidOperationException("NZD must exist before seeding the demo tenant.");

        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == DemoTenantCode, cancellationToken);
        if (tenant is null)
        {
            tenant = Tenant.Create(DemoTenantName, DemoTenantCode, nzd);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsurePersonAsync(
            db,
            userManager,
            UserRole.TenantAdmin,
            name: "Tenant Admin",
            email: TenantAdminEmail,
            password: TenantAdminPassword,
            tenant.Id,
            cancellationToken);

        await EnsurePersonAsync(
            db,
            userManager,
            UserRole.Adviser,
            name: "Demo Adviser",
            email: AdviserEmail,
            password: AdviserPassword,
            tenant.Id,
            cancellationToken);
    }

    private static async Task EnsurePersonAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        UserRole role,
        string name,
        string email,
        string password,
        int? tenantId,
        CancellationToken cancellationToken)
    {
        var exists = tenantId is null
            ? await db.DomainUsers.AnyAsync(
                user => user.Role == role && user.Email == email, cancellationToken)
            : await db.DomainUsers.AnyAsync(
                user => user.Role == role && user.Email == email && user.TenantId == tenantId,
                cancellationToken);
        if (exists)
        {
            return;
        }

        var publicId = Guid.NewGuid();
        var identityUser = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };

        var result = await userManager.CreateAsync(identityUser, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        var person = User.Create(
            role,
            name,
            email,
            identityUser.Id,
            tenantId,
            publicId: publicId);

        db.DomainUsers.Add(person);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsTestHost()
    {
        var entry = Assembly.GetEntryAssembly()?.GetName().Name;
        return entry is not null
               && (entry.Contains("testhost", StringComparison.OrdinalIgnoreCase)
                   || entry.Contains("vstest", StringComparison.OrdinalIgnoreCase));
    }
}
