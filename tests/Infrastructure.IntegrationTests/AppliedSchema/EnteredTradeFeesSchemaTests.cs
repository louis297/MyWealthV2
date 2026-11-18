using Microsoft.Data.SqlClient;
using MyWealthV2.Infrastructure.Data.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>External caller numbers §6: a <c>Brokerage</c> fee detail may have no schedule (script 0034).</summary>
public class EnteredTradeFeesSchemaTests
{
    private const string EnteredTradeFeesScript = "0034_entered_trade_fees.sql";

    private const string ScheduledDetail = """
        INSERT INTO [TransactionFeeDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount], [Currency])
        VALUES (@a_feeleg, @a_tenant, @a_buy, 0, @a_feeschedule, 1250, 10, 'USD');
        """;

    private const string DropSchedule = "UPDATE [TransactionFeeDetails] SET [TradeFeeScheduleId] = NULL;";

    [Test]
    public void SchemaDirectory_Includes0034EnteredTradeFeesScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(EnteredTradeFeesScript);
    }

    [Test]
    public async Task Apply0034_KeepsExistingFeeDetailsAndAllowsABrokerageFeeWithoutSchedule()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("EnteredTradeFees");
        await db.ApplyBeforeAsync(EnteredTradeFeesScript);
        await db.ExecuteAsync(
            ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
            + ScratchSchemaDatabase.FeeRows("a")
            + ScheduledDetail);
        const string read = "SELECT [CashLegId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount] FROM [TransactionFeeDetails]";
        var before = await db.ReadAsync(read);

        // 547: the CHECK of 0030 refuses a Brokerage fee without a schedule.
        (await Should.ThrowAsync<SqlException>(() => db.ExecuteAsync(DropSchedule))).Number.ShouldBe(547);

        await db.ApplyAllAsync();

        var after = await db.ReadAsync(read);
        before.Count.ShouldBe(1);
        after.Single().ShouldBe(before.Single());
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{EnteredTradeFeesScript}'"))
            .Count.ShouldBe(1);

        await db.ExecuteAsync(DropSchedule);
        (await db.ReadAsync("SELECT [TradeFeeScheduleId] FROM [TransactionFeeDetails]"))
            .Single()[0].ShouldBe(DBNull.Value);
    }
}
