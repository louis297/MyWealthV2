using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Client money accounts §6.2: <c>Accounts.DepositReference</c> (script 0039).</summary>
public class DepositReferenceSchemaTests
{
    private const string DepositReferenceScript = "0039_accounts_deposit_reference.sql";

    [Test]
    public void SchemaDirectory_Includes0039Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(DepositReferenceScript);
    }

    [Test]
    public async Task Apply_DepositReferenceIsANullableChar9()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'Accounts' AND COLUMN_NAME = N'DepositReference'
            """);

        rows.Count.ShouldBe(1);
        rows[0][0].ShouldBe("char");
        rows[0][1].ShouldBe(9);
        rows[0][2].ShouldBe("YES");
    }

    [Test]
    public async Task Apply0039_BackFillsEveryCustodialAccountWithAValidDistinctReference_AndLeavesExternalNull()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("DepositReferenceBackfill");
        await db.ApplyBeforeAsync(DepositReferenceScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("a", Code()) + CustodialAccounts("a", 40));
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("b", Code()) + CustodialAccounts("b", 25));

        await db.ApplyAllAsync();

        var rows = await db.ReadAsync("SELECT [CustodyMode], [DepositReference] FROM [Accounts] ORDER BY [Id]");
        var custodial = rows.Where(row => (int)row[0] == 1).Select(row => row[1]).ToList();
        var external = rows.Where(row => (int)row[0] == 0).Select(row => row[1]).ToList();
        custodial.Count.ShouldBe(65);
        external.Count.ShouldBe(4);
        external.ShouldAllBe(value => value is DBNull);
        var references = custodial.Select(value => (string)value).ToList();
        references.ShouldAllBe(value => DepositReference.IsValid(value));
        references.Distinct().Count().ShouldBe(references.Count);
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{DepositReferenceScript}'"))
            .Count.ShouldBe(1);
    }

    [Test]
    public async Task CustodialInsertWithoutAReference_ViolatesTheCheck()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(
            ScratchSchemaDatabase.TenantRows("a", Code()) + InsertAccount(custodyMode: 1, reference: null)));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Accounts_DepositReference");
    }

    [Test]
    public async Task ExternalInsertWithAReference_ViolatesTheCheck()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(
            ScratchSchemaDatabase.TenantRows("a", Code()) + InsertAccount(custodyMode: 0, reference: Valid())));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Accounts_DepositReference");
    }

    [Test]
    public async Task DuplicateReference_ViolatesTheUniqueIndex()
    {
        var reference = Valid();
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync(
            ScratchSchemaDatabase.TenantRows("a", Code())
            + InsertAccount(custodyMode: 1, reference)
            + ScratchSchemaDatabase.TenantRows("b", Code())
            + InsertAccount(custodyMode: 1, reference, prefix: "b")));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_Accounts_DepositReference");
    }

    [Test]
    public async Task CustodialInsertWithAReference_AndExternalWithout_Insert()
    {
        var count = await RunAsync(
            ScratchSchemaDatabase.TenantRows("a", Code())
            + InsertAccount(custodyMode: 1, Valid())
            + InsertAccount(custodyMode: 0, reference: null)
            + "SELECT COUNT(*) FROM [Accounts] WHERE [TenantId] = @a_tenant;");

        count.ShouldBe(4);
    }

    [Test]
    public async Task EfRoundTrip_KeepsTheReference_AndNullOnExternal()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("DepositReferenceEf");
        await db.ApplyAllAsync();
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("a", Code()));
        var ids = await db.ReadAsync("SELECT TOP 1 [TenantId], [Id] FROM [Users] WHERE [Role] = 3");
        var tenantId = (int)ids[0][0];
        var customerId = (int)ids[0][1];
        var reference = DepositReference.TryParse(Valid(), out var parsed) ? parsed : throw new InvalidOperationException();
        var usd = Currency.Create("USD", "US Dollar", 2);
        var custodial = Account.Create(tenantId, customerId, "Managed", AccountType.Brokerage, CustodyMode.Custodial, usd, reference);
        var external = Account.Create(tenantId, customerId, "Held elsewhere", AccountType.Bank, CustodyMode.External, usd);
        custodial.CreatedBy = "test";
        external.CreatedBy = "test";

        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Accounts.AddRange(custodial, external);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            (await context.Accounts.AsNoTracking().SingleAsync(row => row.PublicId == custodial.PublicId))
                .DepositReference.ShouldBe(reference);
            (await context.Accounts.AsNoTracking().SingleAsync(row => row.PublicId == external.PublicId))
                .DepositReference.ShouldBeNull();
            (await context.Accounts.AsNoTracking().CountAsync(row => row.DepositReference! == reference)).ShouldBe(1);
        }
    }

    private static string Code() => Guid.NewGuid().ToString("N")[..12];

    private static readonly Random Seeded = new();

    /// <summary>A valid reference made by the Domain, so the C# and the T-SQL check symbol meet in these tests.</summary>
    private static string Valid() => DepositReference.Generate(new SystemRandomSymbols(Seeded)).Value;

    /// <summary>Custodial Brokerage accounts in USD for the tenant of <paramref name="prefix"/>, inserted before 0039.</summary>
    private static string CustodialAccounts(string prefix, int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"""

            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [CustodyMode], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), @{prefix}_tenant, @{prefix}_customer, N'Managed {i}', 2, 1, {i % 2}, 'USD', SYSDATETIMEOFFSET(), N'test');
            """));

    private static string InsertAccount(int custodyMode, string? reference, string prefix = "a") => $"""

        INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [CustodyMode], [Status], [Currency], [DepositReference], [Created], [CreatedBy])
        VALUES (NEWID(), @{prefix}_tenant, @{prefix}_customer, N'New', 2, {custodyMode}, 0, 'USD', {(reference is null ? "NULL" : $"'{reference}'")}, SYSDATETIMEOFFSET(), N'test');
        """;

    private static ApplicationDbContext OpenDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

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

    private sealed class SystemRandomSymbols(Random random) : IRandomSymbols
    {
        public int Next(int maxExclusive) => random.Next(maxExclusive);
    }
}
