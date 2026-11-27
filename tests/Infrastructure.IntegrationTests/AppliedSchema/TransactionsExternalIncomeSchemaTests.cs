using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Income on External accounts §6: a dividend detail may have no amount per share and no ex-date
/// (script 0060).</summary>
[Parallelizable]
public class TransactionsExternalIncomeSchemaTests
{
    private const string Script = "0060_external_income.sql";

    private const string ReadDetails = """
        SELECT [TransactionId], [Quantity], [AmountPerShare], [ExDate], [RecordDate], [PaymentDate]
        FROM [TransactionDividendDetails] ORDER BY [TransactionId]
        """;

    [Test]
    public void SchemaDirectory_Includes0060Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply0060_KeepsExistingDetails_AndAllowsNoAmountPerShareAndNoExDate()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("ExternalIncome");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync(
            ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
            + DividendDetail("@a_buy", "1.25", "'2026-11-26'"));
        var before = await db.ReadAsync(ReadDetails);

        // 515: before 0060 both columns are NOT NULL.
        (await Should.ThrowAsync<SqlException>(() => db.ExecuteAsync(
            "UPDATE [TransactionDividendDetails] SET [AmountPerShare] = NULL;"))).Number.ShouldBe(515);

        await db.ApplyAllAsync();

        (await db.ReadAsync(ReadDetails)).Single().ShouldBe(before.Single());
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{Script}'")).Count.ShouldBe(1);

        await Should.NotThrowAsync(() => db.ExecuteAsync(
            "UPDATE [TransactionDividendDetails] SET [AmountPerShare] = NULL, [ExDate] = NULL;"));
        var after = (await db.ReadAsync(ReadDetails)).Single();
        after[2].ShouldBe(DBNull.Value);
        after[3].ShouldBe(DBNull.Value);
    }

    [Test]
    public async Task ADividendDetail_WithAnAmountPerShareOf0_Violates()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            {DividendDetail("@a_buy", "0", "NULL")}
            """));
        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_TransactionDividendDetails_AmountPerShare");
    }

    [Test]
    public async Task ADividendDetail_WithNoAmountPerShareAndNoExDate_ReadsThroughEf()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("ExternalIncomeEf");
        await db.ApplyAllAsync();
        await Should.NotThrowAsync(() => db.ExecuteAsync(
            ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
            + DividendDetail("@a_buy", "NULL", "NULL")
            + DividendDetail("@a_fx", "0.26", "'2026-11-09'")));

        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(db.ConnectionString).Options);
        List<TransactionDividendDetail> details = [];
        await Should.NotThrowAsync(async () => details = await context.Set<TransactionDividendDetail>()
            .IgnoreQueryFilters().AsNoTracking()
            .OrderBy(detail => detail.TransactionId)
            .ToListAsync());
        details.Select(detail => ((object?)detail.AmountPerShare, (object?)detail.ExDate)).ShouldBe(
        [
            (null, null),
            (0.26m, new DateOnly(2026, 11, 9))
        ]);
    }

    private static string DividendDetail(string transaction, string amountPerShare, string exDate) => $"""
        INSERT INTO [TransactionDividendDetails]
            ([TransactionId], [TenantId], [InstrumentId], [Quantity], [AmountPerShare], [Currency], [TaxSource],
             [ImputationCredit], [ReinvestedQuantity], [ReinvestmentPrice], [ExDate], [RecordDate], [PaymentDate])
        VALUES ({transaction}, @a_tenant, @a_instrument, 10, {amountPerShare}, 'USD', 1, 0, NULL, NULL,
                {exDate}, '2026-11-10', '2026-11-13');
        """;

    /// <summary>Runs <paramref name="statement"/> on the schema test database, then rolls back.</summary>
    private static async Task<object?> RunAsync(string statement)
    {
        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
