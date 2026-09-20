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
            .IsActive.ShouldBeFalse();
        (await db.Tenants.AsNoTracking().SingleAsync(row => row.Id == tenantB.Id))
            .IsActive.ShouldBeTrue();
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

    [Test]
    public async Task Advisers_TenantADoesNotIncludeB()
    {
        await using var db = OpenDb();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adviserA, adviserB) = await TwoTenantFixture.InsertAdvisersAsync(db, tenantA, tenantB);

        var onlyA = await db.DomainUsers.AsNoTracking()
            .Where(person => person.Role == UserRole.Adviser && person.TenantId == tenantA.Id)
            .Select(person => person.PublicId)
            .ToListAsync();
        onlyA.ShouldBe([adviserA.PublicId]);
        onlyA.ShouldNotContain(adviserB.PublicId);

        var otherTenant = await db.DomainUsers.AsNoTracking()
            .SingleOrDefaultAsync(person =>
                person.PublicId == adviserB.PublicId
                && person.Role == UserRole.Adviser
                && person.TenantId == tenantA.Id);
        otherTenant.ShouldBeNull();
    }

    [Test]
    public async Task Customers_TenantADoesNotIncludeB()
    {
        await using var db = OpenDb();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adviserA, adviserB) = await TwoTenantFixture.InsertAdvisersAsync(db, tenantA, tenantB);
        var (customerA, customerB) = await TwoTenantFixture.InsertCustomersAsync(
            db, tenantA, tenantB, adviserA, adviserB);

        var onlyA = await db.DomainUsers.AsNoTracking()
            .Where(person => person.Role == UserRole.Customer && person.TenantId == tenantA.Id)
            .Select(person => person.PublicId)
            .ToListAsync();
        onlyA.ShouldBe([customerA.PublicId]);
        onlyA.ShouldNotContain(customerB.PublicId);

        var otherTenant = await db.DomainUsers.AsNoTracking()
            .SingleOrDefaultAsync(person =>
                person.PublicId == customerB.PublicId
                && person.Role == UserRole.Customer
                && person.TenantId == tenantA.Id);
        otherTenant.ShouldBeNull();
    }

    [Test]
    public async Task TwoAdvisersInOneTenant_ADoesNotSeeBAssigned()
    {
        await using var db = OpenDb();
        var (tenantA, _) = await TwoTenantFixture.InsertAsync(db);
        var (adviserA, adviserB, customerA, customerB) =
            await TwoTenantFixture.InsertTwoAdvisersWithCustomersAsync(db, tenantA);

        var assignedToA = await db.DomainUsers.AsNoTracking()
            .Where(person =>
                person.Role == UserRole.Customer
                && person.TenantId == tenantA.Id
                && person.AdviserId == adviserA.Id)
            .Select(person => person.PublicId)
            .ToListAsync();
        assignedToA.ShouldBe([customerA.PublicId]);
        assignedToA.ShouldNotContain(customerB.PublicId);
        assignedToA.ShouldNotContain(adviserB.PublicId);
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
