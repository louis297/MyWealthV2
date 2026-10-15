using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class ReportsSchemaTests
{
    [Test]
    public async Task Apply_SeedsTwoReportsAndNoTenantGrants()
    {
        var rows = await QueryReports();

        rows.ShouldBe([
            (
                Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22"),
                "adviser-balances",
                "Adviser Balances Report",
                "Point-in-time cash and holding lines for an adviser's customers.",
                "Book",
                """["manual","scheduled"]""",
                true),
            (
                Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11"),
                "customer-transactions",
                "Customer Transactions Report",
                "Transactions in a date range for one customer.",
                "Customer",
                """["manual","scheduled"]""",
                true)
        ]);

        (await Scalar("SELECT COUNT(*) FROM [TenantReports]")).ShouldBe(0);
    }

    [Test]
    public async Task Apply_CreatesReportsWithoutTenantOrAudit()
    {
        var columns = await LoadColumns("Reports");

        columns.Select(column => column.Name).ShouldBe([
            "Id", "PublicId", "Code", "Name", "Function", "Scope", "AllowedTriggers", "IsActive"
        ]);
        Column(columns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(columns, "PublicId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("uniqueidentifier"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Code").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(64),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Name").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(200),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Function").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(500),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Scope").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(20),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "AllowedTriggers").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(200),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "IsActive").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("bit"),
            column => column.Nullable.ShouldBe("NO"));
    }

    [Test]
    public async Task Apply_CreatesTenantReportsAsGrantPairs()
    {
        var columns = await LoadColumns("TenantReports");

        columns.Select(column => column.Name).ShouldBe(["Id", "TenantId", "ReportId"]);
        Column(columns, "Id").Identity.ShouldBe(1);
        Column(columns, "TenantId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "ReportId").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
    }

    [Test]
    public async Task Apply_CreatesSchedulesAndExecutionsWithAudit()
    {
        var schedules = await LoadColumns("ReportSchedules");
        schedules.Select(column => column.Name).ShouldBe([
            "Id", "PublicId", "ReportId", "TenantId", "ParametersJson", "IsActive", "Cron",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);
        Column(schedules, "ParametersJson").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(-1),
            column => column.Nullable.ShouldBe("NO"));
        Column(schedules, "Cron").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(100),
            column => column.Nullable.ShouldBe("NO"));
        Column(schedules, "IsActive").Type.ShouldBe("bit");
        Column(schedules, "Created").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("datetimeoffset"),
            column => column.Nullable.ShouldBe("NO"));
        Column(schedules, "CreatedBy").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(450),
            column => column.Nullable.ShouldBe("NO"));
        Column(schedules, "LastModified").Nullable.ShouldBe("YES");
        Column(schedules, "LastModifiedBy").Nullable.ShouldBe("YES");
        Column(schedules, "RowVersion").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("timestamp"),
            column => column.Nullable.ShouldBe("NO"));

        var executions = await LoadColumns("ReportExecutions");
        executions.Select(column => column.Name).ShouldBe([
            "Id", "PublicId", "ReportId", "ScheduleId", "TenantId", "TriggerKind", "Status",
            "ParametersJson", "AsOf", "TriggeredByUserId", "ResultStorageKey", "ErrorMessage",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);
        Column(executions, "ScheduleId").Nullable.ShouldBe("YES");
        Column(executions, "TriggerKind").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(20),
            column => column.Nullable.ShouldBe("NO"));
        Column(executions, "Status").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(20),
            column => column.Nullable.ShouldBe("NO"));
        Column(executions, "ParametersJson").Length.ShouldBe(-1);
        Column(executions, "AsOf").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("datetimeoffset"),
            column => column.Nullable.ShouldBe("YES"));
        Column(executions, "TriggeredByUserId").Nullable.ShouldBe("YES");
        Column(executions, "ResultStorageKey").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(500),
            column => column.Nullable.ShouldBe("YES"));
        Column(executions, "ErrorMessage").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(2000),
            column => column.Nullable.ShouldBe("YES"));
        Column(executions, "RowVersion").Type.ShouldBe("timestamp");
    }

    [Test]
    public async Task Apply_AddsUniquesChecksAndForeignKeysWithoutCascade()
    {
        (await UniqueColumns("Reports")).ShouldBe([
            ("UQ_Reports_Code", "Code"),
            ("UQ_Reports_PublicId", "PublicId")
        ]);
        (await UniqueColumns("TenantReports")).ShouldBe([
            ("UQ_TenantReports_TenantId_ReportId", "TenantId"),
            ("UQ_TenantReports_TenantId_ReportId", "ReportId")
        ]);
        (await UniqueColumns("ReportSchedules")).ShouldBe([
            ("UQ_ReportSchedules_PublicId", "PublicId")
        ]);
        (await UniqueColumns("ReportExecutions")).ShouldBe([
            ("UQ_ReportExecutions_PublicId", "PublicId")
        ]);

        (await IndexColumns("ReportSchedules", "IX_ReportSchedules_TenantId_ReportId"))
            .ShouldBe(["TenantId", "ReportId"]);
        (await IndexColumns("ReportExecutions", "IX_ReportExecutions_TenantId_ReportId"))
            .ShouldBe(["TenantId", "ReportId"]);

        var scope = await CheckDefinition("Reports", "CK_Reports_Scope");
        scope.ShouldContain("Customer");
        scope.ShouldContain("Book");

        var trigger = await CheckDefinition("ReportExecutions", "CK_ReportExecutions_TriggerKind");
        trigger.ShouldContain("Manual");
        trigger.ShouldContain("Scheduled");
        trigger.ShouldContain("Event");

        var status = await CheckDefinition("ReportExecutions", "CK_ReportExecutions_Status");
        status.ShouldContain("Queued");
        status.ShouldContain("Running");
        status.ShouldContain("Succeeded");
        status.ShouldContain("Failed");

        (await ForeignKeys("TenantReports")).ShouldBe([
            ("FK_TenantReports_Reports_ReportId", "Reports", "Id", (byte)0),
            ("FK_TenantReports_Tenants_TenantId", "Tenants", "Id", (byte)0)
        ]);
        (await ForeignKeys("ReportSchedules")).ShouldBe([
            ("FK_ReportSchedules_Reports_ReportId", "Reports", "Id", (byte)0),
            ("FK_ReportSchedules_Tenants_TenantId", "Tenants", "Id", (byte)0)
        ]);
        (await ForeignKeys("ReportExecutions")).ShouldBe([
            ("FK_ReportExecutions_Reports_ReportId", "Reports", "Id", (byte)0),
            ("FK_ReportExecutions_ReportSchedules_ScheduleId", "ReportSchedules", "Id", (byte)0),
            ("FK_ReportExecutions_Tenants_TenantId", "Tenants", "Id", (byte)0),
            ("FK_ReportExecutions_Users_TriggeredByUserId", "Users", "Id", (byte)0)
        ]);
    }

    private static async Task<List<(Guid PublicId, string Code, string Name, string Function, string Scope, string AllowedTriggers, bool IsActive)>> QueryReports()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT [PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers], [IsActive]
            FROM [Reports]
            ORDER BY [Code]
            """;

        var rows = new List<(Guid, string, string, string, string, string, bool)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return rows;
    }

    private static async Task<int> Scalar(string sql)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<List<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity)>> LoadColumns(
        string tableName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE,
                   COLUMNPROPERTY(OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity')
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);

        var columns = new List<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                reader.GetString(5),
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6)));
        }

        return columns;
    }

    private static (string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity) Column(
        IReadOnlyList<(string Name, string Type, int? Length, int? Precision, int? Scale, string Nullable, int Identity)> columns,
        string name) =>
        columns.Single(column => column.Name == name);

    private static async Task<List<(string Constraint, string Column)>> UniqueColumns(string tableName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT kc.name, COL_NAME(ic.object_id, ic.column_id)
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            WHERE kc.parent_object_id = OBJECT_ID(@tableName) AND kc.type = 'UQ'
            ORDER BY kc.name, ic.key_ordinal
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);

        var rows = new List<(string Constraint, string Column)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<string>> IndexColumns(string tableName, string indexName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.object_id = OBJECT_ID(@tableName) AND i.name = @name
            ORDER BY ic.key_ordinal
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);
        var name = command.CreateParameter();
        name.ParameterName = "@name";
        name.Value = indexName;
        command.Parameters.Add(name);

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<string> CheckDefinition(string tableName, string checkName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT cc.definition
            FROM sys.check_constraints cc
            WHERE cc.parent_object_id = OBJECT_ID(@tableName) AND cc.name = @name
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);
        var name = command.CreateParameter();
        name.ParameterName = "@name";
        name.Value = checkName;
        command.Parameters.Add(name);

        var definition = await command.ExecuteScalarAsync();
        definition.ShouldNotBeNull();
        return (string)definition!;
    }

    private static async Task<List<(string Name, string Referenced, string Column, byte DeleteAction)>> ForeignKeys(
        string tableName)
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT fk.name, OBJECT_NAME(fk.referenced_object_id),
                   COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id),
                   fk.delete_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            WHERE fk.parent_object_id = OBJECT_ID(@tableName)
            ORDER BY fk.name
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@tableName";
        table.Value = tableName;
        command.Parameters.Add(table);

        var rows = new List<(string Name, string Referenced, string Column, byte DeleteAction)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3)));
        }

        return rows;
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
