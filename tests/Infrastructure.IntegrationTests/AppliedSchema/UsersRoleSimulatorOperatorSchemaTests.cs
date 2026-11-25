using Microsoft.Data.SqlClient;
using MyWealthV2.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Simulator spec §6.1: <c>CK_Users_RoleShape</c> lets Role 4 (SimulatorOperator) have no tenant and no adviser (script 0036).</summary>
[Parallelizable]
public class UsersRoleSimulatorOperatorSchemaTests
{
    private const string Script = "0036_users_role_simulator_operator.sql";

    [Test]
    public void SchemaDirectory_Includes0036Script()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(Script);
    }

    [Test]
    public async Task Insert_OperatorWithoutTenantOrAdviser_Succeeds()
    {
        var code = Code();
        var rows = await RunAsync($"""
            {IdentityRow(code, tenant: "NULL")}
            {UserRow(code, role: 4, tenant: "NULL", adviser: "NULL")}
            SELECT COUNT(*) FROM [Users] WHERE [IdentityUserId] = N'{code}';
            """);

        rows.ShouldBe(1);
    }

    [Test]
    public async Task Insert_OperatorWithTenant_ViolatesTheCheck()
    {
        var code = Code();
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {TenantRow(code)}
            {IdentityRow(code, tenant: "@tenant")}
            {UserRow(code, role: 4, tenant: "@tenant", adviser: "NULL")}
            """));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain("CK_Users_RoleShape");
    }

    [Test]
    public async Task Insert_SystemAdminWithoutTenant_StillSucceeds()
    {
        var code = Code();
        var rows = await RunAsync($"""
            {IdentityRow(code, tenant: "NULL")}
            {UserRow(code, role: 0, tenant: "NULL", adviser: "NULL")}
            SELECT COUNT(*) FROM [Users] WHERE [IdentityUserId] = N'{code}';
            """);

        rows.ShouldBe(1);
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task Insert_FirmStaffWithTenant_StillSucceeds(int role)
    {
        var code = Code();
        var rows = await RunAsync($"""
            {TenantRow(code)}
            {IdentityRow(code, tenant: "@tenant")}
            {UserRow(code, role, tenant: "@tenant", adviser: "NULL")}
            SELECT COUNT(*) FROM [Users] WHERE [IdentityUserId] = N'{code}';
            """);

        rows.ShouldBe(1);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public async Task Insert_ExistingRoleWithWrongShape_IsStillRejected(int role)
    {
        // 0 with a tenant, 1 without one, 3 without an adviser.
        var code = Code();
        var tenant = role == 1 ? "NULL" : "@tenant";
        var exception = await Should.ThrowAsync<SqlException>(() => RunAsync($"""
            {TenantRow(code)}
            {IdentityRow(code, tenant)}
            {UserRow(code, role, tenant, adviser: "NULL")}
            """));

        exception.Number.ShouldBe(547);
    }

    private static string TenantRow(string code) => $"""
        INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency])
        VALUES (NEWID(), N'Tenant {code}', N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD');
        DECLARE @tenant int = SCOPE_IDENTITY();
        """;

    private static string IdentityRow(string code, string tenant) => $"""
        INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [TenantId])
        VALUES (N'{code}', 1, 0, 0, 0, 0, {tenant});
        """;

    private static string UserRow(string code, int role, string tenant, string adviser) => $"""
        INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
        VALUES (NEWID(), {tenant}, N'Person', N'{code}@example.test', {role}, 1, {adviser}, N'{code}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        """;

    private static string Code() => Guid.NewGuid().ToString("N")[..12];

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
