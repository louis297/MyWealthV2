using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Currencies;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class CurrencyCatalogTests
{
    [Test]
    public async Task TryGet_NormalisesCodeAndReturnsNullWhenMissing()
    {
        var catalog = await LoadCatalog();

        catalog.TryGet("nzd")!.Code.ShouldBe("NZD");
        catalog.TryGet("XXX").ShouldBeNull();
        catalog.IsEnabled("nzd").ShouldBeTrue();
        catalog.IsEnabled("XXX").ShouldBeFalse();
    }

    [Test]
    public async Task List_EnabledOnly_OmitsDisabledRowsAfterReload()
    {
        var catalog = await LoadCatalog();
        await using var db = OpenDb();

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO [Currencies] ([Code], [Name], [DecimalPlaces], [IsActive])
                VALUES ('XXX', N'Test Disabled', 2, 0)
                """);
            await catalog.ReloadAsync();

            catalog.List(enabledOnly: true).Select(row => row.Code).ShouldNotContain("XXX");
            catalog.List(enabledOnly: false).Select(row => row.Code).ShouldContain("XXX");
            catalog.IsEnabled("XXX").ShouldBeFalse();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [Currencies] WHERE [Code] = 'XXX'");
            await catalog.ReloadAsync();
        }
    }

    [Test]
    public async Task List_IsSortedByCode()
    {
        var catalog = await LoadCatalog();

        catalog.List().Select(row => row.Code).ShouldBe(["AUD", "EUR", "GBP", "JPY", "NZD", "USD"]);
    }

    private static async Task<ICurrencyCatalog> LoadCatalog()
    {
        var catalog = new CurrencyCatalog(CreateScopeFactory());
        await catalog.ReloadAsync();
        return catalog;
    }

    private static IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(SchemaDatabaseSetup.ConnectionString));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
