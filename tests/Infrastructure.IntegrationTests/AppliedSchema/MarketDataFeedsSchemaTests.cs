using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Simulated instruments §6.1: <c>MarketDataFeeds</c>, one row per connection and feed kind (script 0049).</summary>
public class MarketDataFeedsSchemaTests
{
    private const string Script = "0049_market_data_feeds.sql";

    [Test]
    public void SchemaDirectory_Includes0049Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task OneRowPerConnectionAndKind_Inserts()
    {
        var count = await RunAsync("""
            INSERT INTO [MarketDataFeeds] ([Connection], [Kind], [LastSucceededAt], [LastFailedAt], [LastFailure])
            VALUES (N'simulator', 0, SYSDATETIMEOFFSET(), NULL, NULL),
                   (N'other', 0, NULL, SYSDATETIMEOFFSET(), 4);
            SELECT COUNT(*) FROM [MarketDataFeeds];
            """);

        count.ShouldBe(2);
    }

    [Test]
    public async Task ASecondRowForTheSameConnectionAndKind_ViolatesTheKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [MarketDataFeeds] ([Connection], [Kind]) VALUES (N'simulator', 0);
            INSERT INTO [MarketDataFeeds] ([Connection], [Kind]) VALUES (N'simulator', 0);
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("PK_MarketDataFeeds");
    }

    [Test]
    public async Task KindTwo_ViolatesTheKindCheck()
    {
        // Kind 1 (CorporateActions) is allowed since script 0050 (corporate-actions §6.1).
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync("""
            INSERT INTO [MarketDataFeeds] ([Connection], [Kind]) VALUES (N'simulator', 2);
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_MarketDataFeeds_Kind");
    }

    [TestCase(6)]
    [TestCase(-1)]
    public async Task ALastFailureOutsideItsValues_ViolatesTheCheck(int failure)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            INSERT INTO [MarketDataFeeds] ([Connection], [Kind], [LastFailure]) VALUES (N'simulator', 0, {failure});
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_MarketDataFeeds_LastFailure");
    }

    [Test]
    public async Task EfRoundTrip_KeepsTheFeedState()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("MarketDataFeedsEf");
        await db.ApplyAllAsync();
        var at = new DateTimeOffset(2026, 11, 22, 3, 15, 1, TimeSpan.Zero);
        var feed = MarketDataFeed.Start("simulator", MarketDataFeedKind.Prices);
        feed.Succeeded(at);
        feed.Failed(MarketDataFailure.BadResponse, at.AddMinutes(1));
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.MarketDataFeeds.Add(feed);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var read = await context.MarketDataFeeds.SingleAsync();
            read.Connection.ShouldBe("simulator");
            read.Kind.ShouldBe(MarketDataFeedKind.Prices);
            read.Cursor.ShouldBeNull();
            read.LastSucceededAt.ShouldBe(at);
            read.LastFailedAt.ShouldBe(at.AddMinutes(1));
            read.LastFailure.ShouldBe(MarketDataFailure.BadResponse);
            read.RowVersion.ShouldNotBeEmpty();
        }
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
            command.CommandText = statement;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
