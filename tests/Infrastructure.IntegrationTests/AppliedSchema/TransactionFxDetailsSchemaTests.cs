using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyWealthV2.Infrastructure.Data;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>FX conversions §6: TransactionFxDetails shape and constraints.</summary>
public class TransactionFxDetailsSchemaTests
{
    [Test]
    public async Task Insert_ValidRow_IsAccepted()
    {
        await RunInRolledBackTransaction(async (connection, transaction, transactionId) =>
        {
            await InsertDetailAsync(connection, transaction, transactionId, "NZD", "USD", "0.59120000");
            (await CountAsync(connection, transaction, transactionId)).ShouldBe(1);
        });
    }

    [Test]
    public async Task Insert_EqualCurrencies_IsRejected()
    {
        await RunInRolledBackTransaction(async (connection, transaction, transactionId) =>
        {
            var exception = await Should.ThrowAsync<SqlException>(() =>
                InsertDetailAsync(connection, transaction, transactionId, "NZD", "NZD", "1.00000000"));
            exception.Number.ShouldBe(547);
        });
    }

    [TestCase("0")]
    [TestCase("-0.50000000")]
    public async Task Insert_NonPositiveRate_IsRejected(string rate)
    {
        await RunInRolledBackTransaction(async (connection, transaction, transactionId) =>
        {
            var exception = await Should.ThrowAsync<SqlException>(() =>
                InsertDetailAsync(connection, transaction, transactionId, "NZD", "USD", rate));
            exception.Number.ShouldBe(547);
        });
    }

    [Test]
    public async Task Insert_UnknownCurrency_IsRejected()
    {
        await RunInRolledBackTransaction(async (connection, transaction, transactionId) =>
        {
            var exception = await Should.ThrowAsync<SqlException>(() =>
                InsertDetailAsync(connection, transaction, transactionId, "ZZZ", "USD", "1.50000000"));
            exception.Number.ShouldBe(547);
        });
    }

    [Test]
    public async Task Insert_SecondRowForOneTransaction_IsRejected()
    {
        await RunInRolledBackTransaction(async (connection, transaction, transactionId) =>
        {
            await InsertDetailAsync(connection, transaction, transactionId, "NZD", "USD", "0.59120000");
            var exception = await Should.ThrowAsync<SqlException>(() =>
                InsertDetailAsync(connection, transaction, transactionId, "NZD", "AUD", "0.91000000"));
            exception.Number.ShouldBe(2627);
        });
    }

    private static async Task RunInRolledBackTransaction(
        Func<DbConnection, DbTransaction, int, Task> body)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.OpenConnectionAsync();
        await using var efTransaction = await db.Database.BeginTransactionAsync();
        var connection = db.Database.GetDbConnection();
        var transaction = efTransaction.GetDbTransaction();

        await using (var seed = connection.CreateCommand())
        {
            seed.Transaction = transaction;
            seed.CommandText = """
                INSERT INTO [Tenants] ([PublicId], [Name], [Code], [ReportingCurrency], [Created], [LastModified])
                VALUES (NEWID(), N'FX detail tenant', N'FXDETAIL', 'NZD', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

                INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Created], [CreatedBy])
                SELECT NEWID(), [Id], 15, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test'
                FROM [Tenants] WHERE [Code] = N'FXDETAIL';

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            var transactionId = (int)(await seed.ExecuteScalarAsync())!;
            await body(connection, transaction, transactionId);
        }

        await efTransaction.RollbackAsync();
    }

    private static async Task InsertDetailAsync(
        DbConnection connection,
        DbTransaction transaction,
        int transactionId,
        string baseCurrency,
        string quoteCurrency,
        string rate)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO [TransactionFxDetails]
                ([TransactionId], [BaseCurrency], [QuoteCurrency], [Rate], [RateAsOfDate], [RateSource])
            VALUES ({transactionId}, '{baseCurrency}', '{quoteCurrency}', {rate}, '2026-11-09', N'Frankfurter');
            """;
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(DbConnection connection, DbTransaction transaction, int transactionId)
    {
        await using var count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = $"SELECT COUNT(*) FROM [TransactionFxDetails] WHERE [TransactionId] = {transactionId}";
        return (int)(await count.ExecuteScalarAsync())!;
    }
}
