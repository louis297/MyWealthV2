using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class CurrenciesSchemaTests
{
    [Test]
    public async Task Apply_CreatesCurrenciesTableWithSixSeedRows()
    {
        var rows = await QueryCurrencies();

        rows.Select(row => row.Code).ShouldBe(["AUD", "EUR", "GBP", "JPY", "NZD", "USD"]);
        rows.ShouldBe([
            ("AUD", "Australian Dollar", 2, true),
            ("EUR", "Euro", 2, true),
            ("GBP", "Pound Sterling", 2, true),
            ("JPY", "Japanese Yen", 0, true),
            ("NZD", "New Zealand Dollar", 2, true),
            ("USD", "United States Dollar", 2, true)
        ]);
    }

    private static async Task<IReadOnlyList<(string Code, string Name, int DecimalPlaces, bool IsEnabled)>> QueryCurrencies()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT RTRIM([Code]), [Name], [DecimalPlaces], [IsEnabled]
            FROM [Currencies]
            ORDER BY [Code]
            """;

        var rows = new List<(string Code, string Name, int DecimalPlaces, bool IsEnabled)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetByte(2),
                reader.GetBoolean(3)));
        }

        return rows;
    }
}
