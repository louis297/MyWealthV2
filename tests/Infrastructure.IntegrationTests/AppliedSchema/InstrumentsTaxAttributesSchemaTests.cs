using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Instruments;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Instrument-tax-attributes §6: <c>Instruments.IssuerCountry</c> and <c>InstrumentFifTreatments</c> (script 0061).</summary>
[Parallelizable]
public class InstrumentsTaxAttributesSchemaTests
{
    private const string Script = "0061_instrument_tax_attributes.sql";

    [Test]
    public void SchemaDirectory_Includes0061Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [TestCase("NULL")]
    [TestCase("N'NZ'")]
    [TestCase("N'GB'")]
    public async Task AnIssuerCountryOfTheShape_IsStored(string country)
    {
        (await RunAsync($"""
            {World()}
            UPDATE [Instruments] SET [IssuerCountry] = {country} WHERE [Id] = @a_instrument;
            SELECT COUNT(*) FROM [Instruments] WHERE [Id] = @a_instrument;
            """)).ShouldBe(1);
    }

    [TestCase("N'nz'")]
    [TestCase("N'Nz'")]
    [TestCase("N'NZL'")]
    [TestCase("N'N'")]
    [TestCase("N'N1'")]
    [TestCase("N''")]
    public async Task AnIssuerCountryOfAnotherShape_Violates(string country)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            UPDATE [Instruments] SET [IssuerCountry] = {country} WHERE [Id] = @a_instrument;
            """));

        // NZL does not fit char(2): it is truncated (2628) before the CHECK; every other value breaks the CHECK.
        if (country == "N'NZL'")
        {
            exception.Number.ShouldBe(2628);
        }
        else
        {
            exception.Number.ShouldBe(547);
            exception.Message.ShouldContain("CK_Instruments_IssuerCountry");
        }
    }

    [TestCase(1, "NULL")]
    [TestCase(2, "NULL")]
    [TestCase(3, "N'EX 32 unit trust'")]
    [TestCase(4, "NULL")]
    [TestCase(5, "N'A fixed-rate share'")]
    public async Task ARowOfTheShape_Inserts(int treatment, string note)
    {
        (await RunAsync($"""
            {World()}
            {InsertRow("@a_tenant", "@a_instrument", 2027, treatment, note)}
            SELECT COUNT(*) FROM [InstrumentFifTreatments] WHERE [InstrumentId] = @a_instrument;
            """)).ShouldBe(1);
    }

    [TestCase(2027, 0, "NULL", "CK_InstrumentFifTreatments_Treatment")]
    [TestCase(2027, 6, "NULL", "CK_InstrumentFifTreatments_Treatment")]
    [TestCase(2027, 3, "NULL", "CK_InstrumentFifTreatments_Note")]
    [TestCase(2007, 4, "NULL", "CK_InstrumentFifTreatments_TaxYear")]
    [TestCase(2200, 4, "NULL", "CK_InstrumentFifTreatments_TaxYear")]
    public async Task ARowOutsideTheChecks_Violates(int taxYear, int treatment, string note, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRow("@a_tenant", "@a_instrument", taxYear, treatment, note)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ASecondRowForOneTaxYear_ViolatesTheKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertRow("@a_tenant", "@a_instrument", 2027, 4, "NULL")}
            {InsertRow("@a_tenant", "@a_instrument", 2027, 5, "NULL")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("UQ_InstrumentFifTreatments_InstrumentId_TaxYear");
    }

    [Test]
    public async Task TheSameTaxYearOnTwoInstruments_Inserts()
    {
        (await RunAsync($"""
            {World()}
            {InsertRow("@a_tenant", "@a_instrument", 2027, 4, "NULL")}
            {InsertRow("@a_tenant", "@a_other", 2027, 4, "NULL")}
            SELECT COUNT(*) FROM [InstrumentFifTreatments] WHERE [TenantId] = @a_tenant;
            """)).ShouldBe(2);
    }

    [Test]
    public async Task ARowWhoseTenantIsNotItsInstruments_ViolatesTheCompositeKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            {InsertRow("@a_tenant", "@b_instrument", 2027, 4, "NULL")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_InstrumentFifTreatments_Instruments_TenantId_InstrumentId");
    }

    [Test]
    public async Task Apply_LeavesExistingInstrumentsWithNoCountryAndNoRow()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TaxAttributesBackfill");
        await db.ApplyBeforeAsync(Script);
        await db.ExecuteAsync(World());

        await db.ApplyAllAsync();

        var tables = await db.ReadAsync("SELECT OBJECT_ID(N'InstrumentFifTreatments')");
        tables.Single()[0].ShouldNotBe(DBNull.Value);
        var rows = await db.ReadAsync("""
            SELECT COUNT(*), SUM(CASE WHEN [IssuerCountry] IS NULL THEN 1 ELSE 0 END),
                   (SELECT COUNT(*) FROM [InstrumentFifTreatments])
            FROM [Instruments];
            """);
        var counts = rows.Single().Select(Convert.ToInt32).ToArray();
        counts[0].ShouldBeGreaterThan(0);
        counts[1].ShouldBe(counts[0]);
        counts[2].ShouldBe(0);
    }

    [Test]
    public async Task EfRoundTrip_KeepsTheCountryAndTheRowsThroughAChange()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TaxAttributesEf");
        await db.ApplyAllAsync();
        (await db.ReadAsync("SELECT OBJECT_ID(N'InstrumentFifTreatments')")).Single()[0].ShouldNotBe(DBNull.Value);
        var tenantId = Convert.ToInt32((await db.ReadAsync($"""
            {World()}
            SELECT @a_tenant;
            """)).Single()[0]);

        var instrument = Instrument.Create(tenantId, "BHP", "BHP Group", Currency.Create("AUD", "Australian Dollar", 2));
        instrument.CreatedBy = "test";
        instrument.SetTaxAttributes("au",
        [
            new FifTreatmentInput(2026, FifTreatment.ExemptAsxListed, "Checked."),
            new FifTreatmentInput(2027, FifTreatment.ExemptAsxListed, null)
        ], 2027);
        foreach (var row in instrument.FifTreatments)
        {
            row.CreatedBy = "test";
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            context.Instruments.Add(instrument);
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Instruments.Include(row => row.FifTreatments)
                .SingleAsync(row => row.PublicId == instrument.PublicId);
            stored.IssuerCountry.ShouldBe("AU");
            stored.FifTreatments.Select(row => (row.TenantId, row.TaxYear, row.Treatment, row.Note)).OrderBy(row => row.TaxYear).ShouldBe(
            [
                (tenantId, 2026, FifTreatment.ExemptAsxListed, (string?)"Checked."),
                (tenantId, 2027, FifTreatment.ExemptAsxListed, null)
            ]);

            stored.SetTaxAttributes("AU",
            [
                new FifTreatmentInput(2027, FifTreatment.Attributing, "Left the list."),
                new FifTreatmentInput(2028, FifTreatment.Attributing, null)
            ], 2028);
            foreach (var row in stored.FifTreatments.Where(row => row.CreatedBy is null))
            {
                row.CreatedBy = "test";
            }

            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.Instruments.AsNoTracking().Include(row => row.FifTreatments)
                .SingleAsync(row => row.PublicId == instrument.PublicId);
            stored.FifTreatments.Select(row => (row.TaxYear, row.Treatment, row.Note)).OrderBy(row => row.TaxYear).ShouldBe(
            [
                (2027, FifTreatment.Attributing, (string?)"Left the list."),
                (2028, FifTreatment.Attributing, null)
            ]);
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>).</summary>
    private static string World() => ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12]);

    private static string InsertRow(string tenant, string instrument, int taxYear, int treatment, string note) => $"""
        INSERT INTO [InstrumentFifTreatments] ([TenantId], [InstrumentId], [TaxYear], [Treatment], [Note], [Created], [CreatedBy])
        VALUES ({tenant}, {instrument}, {taxYear}, {treatment}, {note}, SYSDATETIMEOFFSET(), N'test');
        """;

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
