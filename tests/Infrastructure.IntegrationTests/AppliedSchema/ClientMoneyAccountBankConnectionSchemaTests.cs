using Microsoft.Data.SqlClient;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Deposit matching §6.1: <c>ClientMoneyAccounts.BankConnection</c> (script 0040).</summary>
public class ClientMoneyAccountBankConnectionSchemaTests
{
    private const string Script = "0040_client_money_accounts_bank_connection.sql";

    [Test]
    public void SchemaDirectory_Includes0040Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Apply0040_SetsSimulatorOnEveryExistingRow_AndLeavesNoDefault()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("BankConnection0040");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync($"""
            DECLARE @tenant int;
            {InsertTenant()}
            {InsertRow("USD", "SIM-0040-USD", withConnection: false)}
            {InsertRow("NZD", "SIM-0040-NZD", withConnection: false)}
            """);

        await db.ApplyAllAsync();

        var column = await db.ReadAsync("""
            SELECT c.[max_length], c.[is_nullable], c.[default_object_id]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'ClientMoneyAccounts') AND c.[name] = N'BankConnection';
            """);
        var shape = column.ShouldHaveSingleItem();
        ((short)shape[0]).ShouldBe((short)100);
        ((bool)shape[1]).ShouldBeFalse();
        ((int)shape[2]).ShouldBe(0);
        var connections = await db.ReadAsync("SELECT [BankConnection] FROM [ClientMoneyAccounts] ORDER BY [Id];");
        connections.Select(row => (string)row[0]).ShouldBe(["simulator", "simulator"]);
        var versions = await db.ReadAsync($"SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'{Script}';");
        ((int)versions.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task InsertWithoutBankConnection_Fails()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {InsertTenant()}
            {InsertRow("USD", "N-" + Guid.NewGuid().ToString("N")[..16], withConnection: false)}
            """));

        exception.Number.ShouldBe(515);
    }

    private static string InsertTenant()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
            VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
            SET @tenant = SCOPE_IDENTITY();
            """;
    }

    private static string InsertRow(string currency, string number, bool withConnection) => $"""
        INSERT INTO [ClientMoneyAccounts]
            ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy]{(withConnection ? ", [BankConnection]" : "")})
        VALUES (NEWID(), @tenant, '{currency}', N'Simulator Bank', N'Client Funds Trust Account', N'{number}', 1,
                SYSDATETIMEOFFSET(), N'test'{(withConnection ? ", N'simulator'" : "")});
        """;

    private static async Task RunAsync(string statement)
    {
        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DECLARE @tenant int;\n" + statement;
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
