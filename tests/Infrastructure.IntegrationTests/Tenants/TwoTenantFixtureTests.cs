using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TwoTenantFixtureTests
{
    [Test]
    public async Task DisableA_LeavesBEnabled()
    {
        await using var db = OpenDb();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);

        tenantA.Disable();
        await db.SaveChangesAsync();

        (await db.Tenants.AsNoTracking().SingleAsync(row => row.Id == tenantA.Id))
            .IsEnabled.ShouldBeFalse();
        (await db.Tenants.AsNoTracking().SingleAsync(row => row.Id == tenantB.Id))
            .IsEnabled.ShouldBeTrue();
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
