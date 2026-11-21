using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Withdrawals §6.1: the <c>NominatedBankAccounts</c> table (script 0042).</summary>
public class NominatedBankAccountsSchemaTests
{
    private const string Script = "0042_nominated_bank_accounts.sql";

    [Test]
    public void SchemaDirectory_Includes0042Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [TestCase(0, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    public async Task SecondOpenRow_ForOneCustomerNumberAndCurrency_ViolatesTheUniqueIndex(int first, int second)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {Seed()}
            {InsertRow(first, "USD", "06-0123")}
            {InsertRow(second, "USD", "0601-23")}
            """));

        exception.Number.ShouldBe(2601);
        exception.Message.ShouldContain("UX_NominatedBankAccounts_Customer_Number");
    }

    [Test]
    public async Task RejectedAndRemovedRows_DoNotCount_AndAnotherCurrencyOrCustomerIsFine()
    {
        var count = await RunAsync($"""
            {Seed()}
            {InsertRow(2, "USD", "0601")}
            {InsertRow(3, "USD", "0601")}
            {InsertRow(3, "USD", "0601")}
            {InsertRow(1, "USD", "0601")}
            {InsertRow(1, "NZD", "0601")}
            {InsertRow(1, "USD", "0601", customer: "@other")}
            SELECT COUNT(*) FROM [NominatedBankAccounts] WHERE [TenantId] = @tenant;
            """);

        count.ShouldBe(6);
    }

    [TestCase(4)]
    [TestCase(-1)]
    public async Task UnknownStatus_ViolatesTheStatusCheck(int status)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {Seed()}
            {InsertRow(status, "USD", "0601")}
            """));

        exception.Number.ShouldBe(547);
    }

    [TestCase(0, true, false, "CK_NominatedBankAccounts_Review")]
    [TestCase(1, false, false, "CK_NominatedBankAccounts_Review")]
    [TestCase(2, false, false, "CK_NominatedBankAccounts_Review")]
    [TestCase(1, true, true, "CK_NominatedBankAccounts_Removal")]
    [TestCase(3, false, false, "CK_NominatedBankAccounts_Removal")]
    public async Task ReviewAndRemoval_MustMatchTheStatus(int status, bool review, bool removal, string check)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {Seed()}
            {InsertRaw(status, review, removal)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(check);
    }

    [Test]
    public async Task RemovedRow_MayHaveAReviewOrNot()
    {
        var count = await RunAsync($"""
            {Seed()}
            {InsertRaw(3, review: true, removal: true)}
            {InsertRaw(3, review: false, removal: true)}
            SELECT COUNT(*) FROM [NominatedBankAccounts] WHERE [TenantId] = @tenant;
            """);

        count.ShouldBe(2);
    }

    [Test]
    public async Task ACustomerOfAnotherTenant_ViolatesTheCustomerKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {Seed()}
            DECLARE @mine int = @tenant;
            {Seed("x")}
            SET @tenant = @mine;
            {InsertRow(0, "USD", "0601", customer: "@x_customer")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_NominatedBankAccounts_Users_TenantId_CustomerId");
    }

    [Test]
    public async Task EfRoundTrip_KeepsEveryField()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("NominatedBankAccountsEf");
        await db.ApplyAllAsync();
        var tenant = Tenant.Create("Round Trip", Guid.NewGuid().ToString("N")[..12], Currency.Create("NZD", "New Zealand Dollar", 2));
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        var (adviser, customer, admin) = await PeopleAsync(db.ConnectionString, tenant.Id);
        var at = new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
        var row = NominatedBankAccount.Add(tenant.Id, customer, Currency.Create("USD", "US Dollar", 2), "ANZ", "J Smith", "06-0123-0456789-00", adviser, at);
        row.Verify(admin, "Bank statement sighted", at.AddMinutes(10));
        row.Remove(adviser, "Closed", at.AddDays(1));
        row.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.NominatedBankAccounts.Add(row);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var reloaded = await context.NominatedBankAccounts.AsNoTracking().SingleAsync(r => r.PublicId == row.PublicId);
            reloaded.TenantId.ShouldBe(tenant.Id);
            reloaded.CustomerId.ShouldBe(customer);
            reloaded.Currency.ShouldBe("USD");
            reloaded.BankName.ShouldBe("ANZ");
            reloaded.AccountName.ShouldBe("J Smith");
            reloaded.AccountNumber.ShouldBe("06-0123-0456789-00");
            reloaded.AccountNumberKey.ShouldBe("060123045678900");
            reloaded.Status.ShouldBe(NominatedBankAccountStatus.Removed);
            reloaded.AddedByUserId.ShouldBe(adviser);
            reloaded.AddedAt.ShouldBe(at);
            reloaded.ReviewedByUserId.ShouldBe(admin);
            reloaded.ReviewNote.ShouldBe("Bank statement sighted");
            reloaded.RemovedByUserId.ShouldBe(adviser);
            reloaded.RemovedAt.ShouldBe(at.AddDays(1));
            reloaded.RemovedNote.ShouldBe("Closed");
            reloaded.RowVersion.ShouldNotBeEmpty();
        }
    }

    /// <summary>Inserts a tenant with an Adviser, a TenantAdmin and two Customers, and sets <c>@tenant</c>,
    /// <c>@{prefix}_adviser</c>, <c>@{prefix}_customer</c> and (for the default prefix) <c>@other</c>.</summary>
    private static string Seed(string prefix = "s")
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        var other = prefix == "s" ? "DECLARE @other int = SCOPE_IDENTITY();" : "";
        return $"""
            INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
            VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
            SET @tenant = SCOPE_IDENTITY();
            INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [TenantId])
            VALUES (N'{code}-a', 1, 0, 0, 0, 0, @tenant), (N'{code}-c', 1, 0, 0, 0, 0, @tenant), (N'{code}-o', 1, 0, 0, 0, 0, @tenant);
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), @tenant, N'Adviser', N'a@{code}.example', 2, 1, NULL, N'{code}-a', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @{prefix}_adviser int = SCOPE_IDENTITY();
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), @tenant, N'Customer', N'c@{code}.example', 3, 1, @{prefix}_adviser, N'{code}-c', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @{prefix}_customer int = SCOPE_IDENTITY();
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), @tenant, N'Other', N'o@{code}.example', 3, 1, @{prefix}_adviser, N'{code}-o', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            {other}
            """;
    }

    private static string InsertRow(int status, string currency, string number, string customer = "@s_customer")
    {
        var review = status is 1 or 2 ? "@s_adviser, SYSDATETIMEOFFSET(), N'Checked'" : "NULL, NULL, NULL";
        var removal = status == 3 ? "@s_adviser, SYSDATETIMEOFFSET(), N'Gone'" : "NULL, NULL, NULL";
        var key = number.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        return $"""
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [ReviewedByUserId], [ReviewedAt], [ReviewNote], [RemovedByUserId], [RemovedAt], [RemovedNote],
                 [Created], [CreatedBy])
            VALUES (NEWID(), @tenant, {customer}, '{currency}', N'ANZ', N'J Smith', N'{number}', N'{key}', {status},
                    @s_adviser, SYSDATETIMEOFFSET(), {review}, {removal}, SYSDATETIMEOFFSET(), N'test');
            """;
    }

    private static string InsertRaw(int status, bool review, bool removal) => $"""
        INSERT INTO [NominatedBankAccounts]
            ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
             [AddedByUserId], [AddedAt], [ReviewedByUserId], [ReviewedAt], [ReviewNote], [RemovedByUserId], [RemovedAt], [RemovedNote],
             [Created], [CreatedBy])
        VALUES (NEWID(), @tenant, @s_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', {status},
                @s_adviser, SYSDATETIMEOFFSET(),
                {(review ? "@s_adviser, SYSDATETIMEOFFSET(), N'Checked'" : "NULL, NULL, NULL")},
                {(removal ? "@s_adviser, SYSDATETIMEOFFSET(), N'Gone'" : "NULL, NULL, NULL")},
                SYSDATETIMEOFFSET(), N'test');
        """;

    private static async Task<(int Adviser, int Customer, int Admin)> PeopleAsync(string connectionString, int tenantId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [TenantId])
            VALUES (N'rt-a', 1, 0, 0, 0, 0, {tenantId}), (N'rt-c', 1, 0, 0, 0, 0, {tenantId}), (N'rt-t', 1, 0, 0, 0, 0, {tenantId});
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), {tenantId}, N'Adviser', N'a@rt.example', 2, 1, NULL, N'rt-a', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @adviser int = SCOPE_IDENTITY();
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), {tenantId}, N'Customer', N'c@rt.example', 3, 1, @adviser, N'rt-c', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @customer int = SCOPE_IDENTITY();
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), {tenantId}, N'Admin', N't@rt.example', 1, 1, NULL, N'rt-t', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            SELECT CONCAT(@adviser, ',', @customer, ',', SCOPE_IDENTITY());
            """;
        var ids = ((string)(await command.ExecuteScalarAsync())!).Split(',').Select(int.Parse).ToArray();
        return (ids[0], ids[1], ids[2]);
    }

    private static ApplicationDbContext OpenDb(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);

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
