using System.Reflection;
using MyWealthV2.Application.Broker;
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

    public const string SimulatorOperatorEmail = "simulator-operator@localhost";
    public const string SimulatorOperatorPassword = SystemAdminPassword;

    public const string DemoTenantCode = DemoCatalog.TenantCode;
    public const string DemoTenantName = DemoCatalog.TenantName;

    public const string TenantAdminEmail = "tenant-admin@localhost";
    public const string TenantAdminPassword = SystemAdminPassword;

    public const string AdviserEmail = "adviser@localhost";
    public const string AdviserPassword = SystemAdminPassword;

    /// <summary>Adjustment review §9.1: a second TenantAdmin of the demo firm, so that an adjustment can be approved.</summary>
    public const string DemoSecondTenantAdminEmail = "tenant-admin-b@localhost";

    public const string SecondTenantCode = "demo2";
    public const string SecondTenantName = "Demo Advisory 2";

    public const string SecondTenantAdminEmail = "tenant-admin2@localhost";
    public const string SecondAdviserEmail = "adviser2@localhost";

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

        await EnsurePersonAsync(
            db,
            userManager,
            UserRole.SimulatorOperator,
            name: "Simulator Operator",
            email: SimulatorOperatorEmail,
            password: SimulatorOperatorPassword,
            tenantId: null,
            cancellationToken);

        // Functional tests reseed this method after Respawn and assert exact tenant/person list counts.
        if (IsTestHost())
        {
            return;
        }

        await SeedFirmsAsync(services, cancellationToken);
    }

    public static async Task SeedFirmsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var catalog = services.GetRequiredService<ICurrencyCatalog>();
        var nzd = catalog.TryGet("NZD")
                  ?? throw new InvalidOperationException("NZD must exist before seeding a development tenant.");

        foreach (var firm in Firms)
        {
            var tenant = await db.Tenants.SingleOrDefaultAsync(
                row => row.Code == firm.Code, cancellationToken);
            if (tenant is null)
            {
                tenant = Tenant.Create(firm.Name, firm.Code, nzd);
                // Orders §9.1: script 0051 gave the firms seeded before it the connection; a firm created now gets
                // it here, when the host has it.
                if (services.GetService<IBrokerConnections>()?.IsConfigured(DemoTenantSeed.BrokerConnection) == true)
                {
                    tenant.SetBrokerConnection(DemoTenantSeed.BrokerConnection);
                }

                db.Tenants.Add(tenant);
                await db.SaveChangesAsync(cancellationToken);
            }

            await EnsurePersonAsync(
                db,
                userManager,
                UserRole.TenantAdmin,
                firm.TenantAdminName,
                firm.TenantAdminEmail,
                TenantAdminPassword,
                tenant.Id,
                cancellationToken);

            await EnsurePersonAsync(
                db,
                userManager,
                UserRole.Adviser,
                firm.AdviserName,
                firm.AdviserEmail,
                AdviserPassword,
                tenant.Id,
                cancellationToken);
        }
    }

    private readonly record struct DevelopmentFirm(
        string Code,
        string Name,
        string TenantAdminEmail,
        string TenantAdminName,
        string AdviserEmail,
        string AdviserName);

    private static readonly DevelopmentFirm[] Firms =
    [
        new(DemoTenantCode, DemoTenantName, TenantAdminEmail, "Tenant Admin", AdviserEmail, "Demo Adviser"),
        new(SecondTenantCode, SecondTenantName, SecondTenantAdminEmail, "Tenant Admin 2", SecondAdviserEmail, "Demo Adviser 2"),
    ];

    internal static async Task EnsurePersonAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        UserRole role,
        string name,
        string email,
        string password,
        int? tenantId,
        CancellationToken cancellationToken,
        int? adviserId = null)
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

        // The identity row and the person row are written together or not at all, so a person that fails a
        // unique index (a platform-level email already in use) leaves no identity row behind.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

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
                adviserId,
                publicId: publicId);

            db.DomainUsers.Add(person);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                db.ChangeTracker.Clear();
                throw;
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static bool IsTestHost()
    {
        var entry = Assembly.GetEntryAssembly()?.GetName().Name;
        return entry is not null
               && (entry.Contains("testhost", StringComparison.OrdinalIgnoreCase)
                   || entry.Contains("vstest", StringComparison.OrdinalIgnoreCase));
    }
}
