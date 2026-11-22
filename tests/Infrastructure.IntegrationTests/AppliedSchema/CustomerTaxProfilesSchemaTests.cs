using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Interest run §6.1: the <c>CustomerTaxProfiles</c> table (script 0047).</summary>
public class CustomerTaxProfilesSchemaTests
{
    private const string Script = "0047_customer_tax_profiles.sql";

    [Test]
    public void SchemaDirectory_Includes0047Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [TestCase(0, 1, "17.5")]
    [TestCase(0, 1, "NULL")]
    [TestCase(0, 0, "NULL")]
    [TestCase(1, 1, "NULL")]
    [TestCase(1, 0, "NULL")]
    [TestCase(0, 1, "10.5")]
    [TestCase(0, 1, "39")]
    public async Task AProfileOfTheShape_Inserts(int residency, int irdHeld, string rate)
    {
        (await RunAsync($"""
            {World()}
            {InsertProfile(residency, irdHeld, rate)}
            SELECT COUNT(*) FROM [CustomerTaxProfiles] WHERE [UserId] = @a_customer;
            """)).ShouldBe(1);
    }

    [TestCase(2, 1, "NULL", "CK_CustomerTaxProfiles_TaxResidency")]
    [TestCase(0, 1, "45", "CK_CustomerTaxProfiles_RwtRatePercent")]
    [TestCase(0, 1, "0", "CK_CustomerTaxProfiles_RwtRatePercent")]
    [TestCase(0, 0, "17.5", "CK_CustomerTaxProfiles_RwtRateNeedsIrd")]
    [TestCase(1, 1, "17.5", "CK_CustomerTaxProfiles_RwtRateNeedsIrd")]
    public async Task AProfileOutsideTheChecks_Violates(int residency, int irdHeld, string rate, string constraint)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertProfile(residency, irdHeld, rate)}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(constraint);
    }

    [Test]
    public async Task ASecondProfileOfOneCustomer_ViolatesTheKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {InsertProfile(0, 0, "NULL")}
            {InsertProfile(0, 1, "33")}
            """));

        exception.Number.ShouldBe(2627);
        exception.Message.ShouldContain("PK_CustomerTaxProfiles");
    }

    [Test]
    public async Task AProfileOfAUserOfAnotherTenant_ViolatesTheCompositeKey()
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {World()}
            {ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])}
            INSERT INTO [CustomerTaxProfiles]
                ([UserId], [TenantId], [TaxResidency], [IrdNumberHeld], [RwtRatePercent], [UpdatedByUserId], [UpdatedAt], [Created], [CreatedBy])
            VALUES (@b_customer, @a_tenant, 0, 0, NULL, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("FK_CustomerTaxProfiles_Users_TenantId_UserId");
    }

    [Test]
    public async Task EfRoundTrip_KeepsAProfileThroughAChange()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("TaxProfilesEf");
        await db.ApplyAllAsync();
        var ids = (await db.ReadAsync($"""
            {World()}
            SELECT @a_tenant, @a_customer, @a_adviser;
            """)).Single().Select(Convert.ToInt32).ToArray();
        var (tenantId, customerId, adviserId) = (ids[0], ids[1], ids[2]);
        var at = new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
        var profile = CustomerTaxProfile.Create(tenantId, customerId, TaxResidency.NewZealand, true, 17.5m, adviserId, at);
        profile.CreatedBy = "test";
        await using (var context = OpenDb(db.ConnectionString))
        {
            context.CustomerTaxProfiles.Add(profile);
            await context.SaveChangesAsync();
        }

        byte[] firstVersion;
        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.CustomerTaxProfiles.SingleAsync(row => row.UserId == customerId);
            stored.RowVersion.ShouldNotBeEmpty();
            firstVersion = stored.RowVersion;
            stored.Set(TaxResidency.Overseas, false, null, adviserId, at.AddDays(1));
            await context.SaveChangesAsync();
        }

        await using (var context = OpenDb(db.ConnectionString))
        {
            var stored = await context.CustomerTaxProfiles.AsNoTracking().SingleAsync(row => row.UserId == customerId);
            stored.TenantId.ShouldBe(tenantId);
            stored.TaxResidency.ShouldBe(TaxResidency.Overseas);
            stored.IrdNumberHeld.ShouldBeFalse();
            stored.RwtRatePercent.ShouldBeNull();
            stored.UpdatedByUserId.ShouldBe(adviserId);
            stored.UpdatedAt.ShouldBe(at.AddDays(1));
            stored.RowVersion.ShouldNotBe(firstVersion);
        }
    }

    /// <summary>A tenant (<c>@a_*</c> from <see cref="ScratchSchemaDatabase.TenantRows"/>).</summary>
    private static string World() => ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12]);

    private static string InsertProfile(int residency, int irdHeld, string rate) => $"""
        INSERT INTO [CustomerTaxProfiles]
            ([UserId], [TenantId], [TaxResidency], [IrdNumberHeld], [RwtRatePercent], [UpdatedByUserId], [UpdatedAt], [Created], [CreatedBy])
        VALUES (@a_customer, @a_tenant, {residency}, {irdHeld}, {rate}, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
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
