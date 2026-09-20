using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Data;

public class UserIsActiveMappingTests
{
    [Test]
    public void UsersIsActive_IsPersistedComputedColumn()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(local);Database=unused;Trusted_Connection=True;")
            .Options;

        using var db = new ApplicationDbContext(options);
        var property = db.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.IsActive));

        property.ShouldNotBeNull();
        property!.ValueGenerated.ShouldBe(ValueGenerated.OnAddOrUpdate);
        property.GetComputedColumnSql().ShouldNotBeNull();
        property.GetComputedColumnSql()!.ShouldContain("Status", Case.Insensitive);
        property.GetComputedColumnSql()!.ShouldContain("1");
        property.GetIsStored().ShouldBe(true);
    }
}
