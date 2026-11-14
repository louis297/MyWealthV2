using MyWealthV2.Infrastructure.Data.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>fixes/utc-instants.md B5: script 0029 moves every stored BookedAt to offset +00:00, instant unchanged.</summary>
public class UtcBookedAtSchemaTests
{
    private const string UtcBookedAtScript = "0029_transactions_booked_at_utc.sql";

    [Test]
    public async Task Apply0029_SwitchesNonUtcOffsetsAndLeavesUtcRowsAlone()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(UtcBookedAtScript);

        await using var db = await ScratchSchemaDatabase.CreateAsync("UtcBookedAt");
        await db.ApplyBeforeAsync(UtcBookedAtScript);
        await db.ExecuteAsync(ScratchSchemaDatabase.TenantRows("tenanta", "tenanta") + """
            UPDATE [Transactions] SET [BookedAt] = '2026-11-14T16:13:00+13:00' WHERE [Type] = 7;
            UPDATE [Transactions] SET [BookedAt] = '2026-11-14T03:13:00+00:00' WHERE [Type] = 16;
            """);
        var before = await ReadAsync(db);

        await db.ApplyAllAsync();

        var after = await ReadAsync(db);
        after.Count.ShouldBe(2);
        after.ShouldAllBe(row => row.Offset == 0);
        after.Select(row => row.BookedAt).ShouldBe(before.Select(row => row.BookedAt));
        before.Single(row => row.Type == 7).Offset.ShouldBe(780);
        after.Single(row => row.Type == 16).RowVersion
            .ShouldBe(before.Single(row => row.Type == 16).RowVersion);
    }

    private static async Task<List<(int Type, DateTimeOffset BookedAt, int Offset, byte[] RowVersion)>> ReadAsync(
        ScratchSchemaDatabase db) =>
        (await db.ReadAsync("""
            SELECT [Type], [BookedAt], DATEPART(TZOFFSET, [BookedAt]), [RowVersion]
            FROM [Transactions]
            ORDER BY [Type]
            """))
        .Select(row => ((int)row[0], (DateTimeOffset)row[1], (int)row[2], (byte[])row[3]))
        .ToList();
}
