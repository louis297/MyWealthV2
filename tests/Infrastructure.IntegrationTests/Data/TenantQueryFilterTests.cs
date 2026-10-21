using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TenantQueryFilterTests
{
    private static readonly Type[] FilteredTypes =
    [
        typeof(User),
        typeof(Instrument),
        typeof(Account),
        typeof(Transaction),
        typeof(Holding),
        typeof(IdempotencyRecord),
        typeof(TenantReport),
        typeof(ReportExecution),
        typeof(ReportSchedule),
        typeof(UserToken)
    ];

    private static readonly Type[] UnfilteredTypes =
    [
        typeof(Tenant),
        typeof(Currency),
        typeof(Report),
        typeof(ApplicationUser),
        typeof(TransactionCashLeg),
        typeof(TransactionSecurityLeg)
    ];

    [Test]
    public async Task EngagedTenant_QueryWithoutWhere_SeesOnlyThatTenant()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adminA, adminB) = await TwoTenantFixture.InsertTenantAdminsAsync(db, tenantA, tenantB);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
        var instrumentA = Instrument.Create(tenantA.Id, $"AAA{Guid.NewGuid():N}"[..8], "Alpha", nzd);
        var instrumentB = Instrument.Create(tenantB.Id, $"BBB{Guid.NewGuid():N}"[..8], "Beta", nzd);
        instrumentA.CreatedBy = "test";
        instrumentB.CreatedBy = "test";
        db.Instruments.AddRange(instrumentA, instrumentB);
        await db.SaveChangesAsync();

        accessor.Engage(tenantA.Id);

        var people = await db.DomainUsers.AsNoTracking()
            .Where(person => person.PublicId == adminA.PublicId || person.PublicId == adminB.PublicId)
            .Select(person => person.PublicId)
            .ToListAsync();
        var instruments = await db.Instruments.AsNoTracking()
            .Where(row => row.PublicId == instrumentA.PublicId || row.PublicId == instrumentB.PublicId)
            .Select(row => row.PublicId)
            .ToListAsync();

        people.ShouldBe([adminA.PublicId]);
        instruments.ShouldBe([instrumentA.PublicId]);
    }

    [Test]
    public void Model_FiltersTenantScopedTypes_AndLeavesPlatformAndIdentityTypes()
    {
        using var db = OpenDb(new TenantIdAccessor());

        foreach (var type in FilteredTypes)
        {
            db.Model.FindEntityType(type)!.GetDeclaredQueryFilters().ShouldNotBeEmpty($"expected a filter on {type.Name}");
        }

        foreach (var type in UnfilteredTypes)
        {
            db.Model.FindEntityType(type)!.GetDeclaredQueryFilters().ShouldBeEmpty($"expected no filter on {type.Name}");
        }
    }

    private static ApplicationDbContext OpenDb(TenantIdAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options, accessor);
    }
}
