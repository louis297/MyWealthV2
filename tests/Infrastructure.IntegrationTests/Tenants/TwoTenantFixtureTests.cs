using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Enums;
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

    [Test]
    public async Task TenantAdmins_UnfilteredSeesBoth_TenantIdASeesOnlyA()
    {
        await using var db = OpenDb();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adminA, adminB) = await TwoTenantFixture.InsertTenantAdminsAsync(db, tenantA, tenantB);

        var all = await db.DomainUsers.AsNoTracking()
            .Where(person => person.Role == UserRole.TenantAdmin)
            .Select(person => person.PublicId)
            .ToListAsync();
        all.ShouldContain(adminA.PublicId);
        all.ShouldContain(adminB.PublicId);

        var onlyA = await db.DomainUsers.AsNoTracking()
            .Where(person => person.Role == UserRole.TenantAdmin && person.TenantId == tenantA.Id)
            .Select(person => person.PublicId)
            .ToListAsync();
        onlyA.ShouldBe([adminA.PublicId]);
        onlyA.ShouldNotContain(adminB.PublicId);
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
