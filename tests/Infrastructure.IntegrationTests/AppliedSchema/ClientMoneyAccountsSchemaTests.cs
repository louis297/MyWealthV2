using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Client money accounts §6.1: the <c>ClientMoneyAccounts</c> table (script 0038).</summary>
public class ClientMoneyAccountsSchemaTests
{
    private const string ClientMoneyAccountsScript = "0038_client_money_accounts.sql";

    [Test]
    public void SchemaDirectory_Includes0038Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(ClientMoneyAccountsScript);
    }

    [Test]
    public async Task SecondActiveRow_InOneTenantAndCurrency_ViolatesTheActiveIndex()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {InsertTenant()}
            {InsertRow("USD", Number(), active: true)}
            {InsertRow("USD", Number(), active: true)}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_ClientMoneyAccounts_TenantId_Currency_Active");
    }

    [Test]
    public async Task SecondInactiveRow_InOneTenantAndCurrency_Inserts()
    {
        var count = await RunAsync($"""
            {InsertTenant()}
            {InsertRow("USD", Number(), active: true)}
            {InsertRow("USD", Number(), active: false)}
            {InsertRow("USD", Number(), active: false)}
            SELECT COUNT(*) FROM [ClientMoneyAccounts] WHERE [TenantId] = @tenant;
            """);

        count.ShouldBe(3);
    }

    [Test]
    public async Task SameAccountNumber_ActiveTwice_ViolatesTheAccountNumberIndex_IgnoringCase()
    {
        var number = Number();
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {InsertTenant()}
            {InsertRow("USD", number, active: true)}
            {InsertTenant()}
            {InsertRow("NZD", number.ToLowerInvariant(), active: true)}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_ClientMoneyAccounts_AccountNumber_Active");
    }

    [Test]
    public async Task SameAccountNumber_OnceActiveAndOnceInactive_Inserts()
    {
        var number = Number();
        var count = await RunAsync($"""
            {InsertTenant()}
            {InsertRow("USD", number, active: false)}
            {InsertRow("NZD", number, active: true)}
            SELECT COUNT(*) FROM [ClientMoneyAccounts] WHERE [AccountNumber] = N'{number}';
            """);

        count.ShouldBe(2);
    }

    [Test]
    public async Task UnknownCurrency_ViolatesTheCurrencyForeignKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {InsertTenant()}
            {InsertRow("XXX", Number(), active: true)}
            """));

        exception.Number.ShouldBe(547);
    }

    [Test]
    public async Task EfRoundTrip_KeepsEveryField()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("ClientMoneyAccountsEf");
        await db.ApplyAllAsync();
        var currency = Currency.Create("USD", "US Dollar", 2);
        var tenant = Tenant.Create("Round Trip", Guid.NewGuid().ToString("N")[..12], Currency.Create("NZD", "New Zealand Dollar", 2));

        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        var row = ClientMoneyAccount.Create(tenant.Id, currency, "Simulator Bank", "Round Trip Client Funds Trust Account", "SIM-RT-USD");
        row.Disable();
        row.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.ClientMoneyAccounts.Add(row);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var reloaded = await context.ClientMoneyAccounts.AsNoTracking().SingleAsync(r => r.PublicId == row.PublicId);
            reloaded.TenantId.ShouldBe(tenant.Id);
            reloaded.Currency.ShouldBe("USD");
            reloaded.BankName.ShouldBe("Simulator Bank");
            reloaded.AccountName.ShouldBe("Round Trip Client Funds Trust Account");
            reloaded.AccountNumber.ShouldBe("SIM-RT-USD");
            reloaded.IsActive.ShouldBeFalse();
            reloaded.RowVersion.ShouldNotBeEmpty();
        }
    }

    private static string Number() => "N-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    /// <summary>Inserts a tenant and sets <c>@tenant</c> (declared by <see cref="RunAsync"/>) to its id.</summary>
    private static string InsertTenant()
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        return $"""
            INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
            VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
            SET @tenant = SCOPE_IDENTITY();
            """;
    }

    private static string InsertRow(string currency, string number, bool active) => $"""
        INSERT INTO [ClientMoneyAccounts]
            ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy])
        VALUES (NEWID(), @tenant, '{currency}', N'Simulator Bank', N'Client Funds Trust Account', N'{number}', {(active ? 1 : 0)},
                SYSDATETIMEOFFSET(), N'test');
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
            command.CommandText = "DECLARE @tenant int;\n" + statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
