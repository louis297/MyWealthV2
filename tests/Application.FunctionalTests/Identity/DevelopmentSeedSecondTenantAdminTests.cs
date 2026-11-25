using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

/// <summary>Adjustment review §9.1: the demo firm has a second TenantAdmin, so that an adjustment can be approved in
/// a Development database.</summary>
[Parallelizable]
public class DevelopmentSeedSecondTenantAdminTests : TestBase
{
    [Test]
    public async Task DemoFirm_HasTenantAdminB_WhoSignsInWithTheDevelopmentPassword_AndIsSeededOnce()
    {
        for (var run = 0; run < 2; run++)
        {
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            await DevelopmentIdentitySeeder.SeedFirmsAsync(scope.ServiceProvider, CancellationToken.None);
        }

        using var read = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var people = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Email == DevelopmentIdentitySeeder.DemoSecondTenantAdminEmail)
            .Select(row => new { row.Name, row.Role, TenantCode = db.Tenants.IgnoreQueryFilters().Single(t => t.Id == row.TenantId).Code })
            .ToListAsync();
        var person = people.ShouldHaveSingleItem();
        person.Name.ShouldBe("Tenant Admin B");
        person.Role.ShouldBe(UserRole.TenantAdmin);
        person.TenantCode.ShouldBe(DevelopmentIdentitySeeder.DemoTenantCode);

        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.DemoSecondTenantAdminEmail,
            DevelopmentIdentitySeeder.TenantAdminPassword,
            DevelopmentIdentitySeeder.DemoTenantCode);
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }
}
