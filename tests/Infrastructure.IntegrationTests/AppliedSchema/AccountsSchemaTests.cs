using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class AccountsSchemaTests
{
    [Test]
    public async Task Apply_CreatesAccountsTableWithExpectedColumns()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMNPROPERTY(
                OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity')
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = N'Accounts'
            ORDER BY ORDINAL_POSITION
            """;

        var columns = new List<(string Name, string Type, int? Length, string Nullable, int Identity)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        columns.Select(column => column.Name).ShouldBe([
            "Id", "PublicId", "TenantId", "CustomerId", "Name", "Type", "Status", "Currency", "IsActive",
            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RowVersion"
        ]);

        Column(columns, "Id").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"),
            column => column.Identity.ShouldBe(1));
        Column(columns, "PublicId").Type.ShouldBe("uniqueidentifier");
        Column(columns, "PublicId").Nullable.ShouldBe("NO");
        Column(columns, "TenantId").Type.ShouldBe("int");
        Column(columns, "TenantId").Nullable.ShouldBe("NO");
        Column(columns, "CustomerId").Type.ShouldBe("int");
        Column(columns, "CustomerId").Nullable.ShouldBe("NO");
        Column(columns, "Name").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(200),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Type").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Status").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("int"),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "Currency").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("char"),
            column => column.Length.ShouldBe(3),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "IsActive").Type.ShouldBe("bit");
        Column(columns, "IsActive").Nullable.ShouldBe("NO");
        Column(columns, "Created").Type.ShouldBe("datetimeoffset");
        Column(columns, "Created").Nullable.ShouldBe("NO");
        Column(columns, "CreatedBy").ShouldSatisfyAllConditions(
            column => column.Type.ShouldBe("nvarchar"),
            column => column.Length.ShouldBe(450),
            column => column.Nullable.ShouldBe("NO"));
        Column(columns, "LastModified").Type.ShouldBe("datetimeoffset");
        Column(columns, "LastModified").Nullable.ShouldBe("YES");
        Column(columns, "LastModifiedBy").Nullable.ShouldBe("YES");
        Column(columns, "RowVersion").Type.ShouldBe("timestamp");
        Column(columns, "RowVersion").Nullable.ShouldBe("NO");
    }

    [Test]
    public async Task Apply_DoesNotSeedAccountRows()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [Accounts]";
        var count = (int)(await command.ExecuteScalarAsync())!;
        count.ShouldBe(0);
    }

    [Test]
    public async Task Apply_AddsUniquePublicId()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT kc.name, COL_NAME(ic.object_id, ic.column_id)
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            WHERE kc.parent_object_id = OBJECT_ID(N'Accounts') AND kc.type = 'UQ'
            ORDER BY kc.name, ic.key_ordinal
            """;

        var rows = new List<(string Constraint, string Column)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        rows.ShouldContain(("UQ_Accounts_PublicId", "PublicId"));
    }

    [Test]
    public async Task Apply_AddsTenantCustomerAndTenantIsActiveIndexes()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();

        (await IndexColumns(db, "IX_Accounts_TenantId_CustomerId")).ShouldBe(["TenantId", "CustomerId"]);
        (await IndexColumns(db, "IX_Accounts_TenantId_IsActive")).ShouldBe(["TenantId", "IsActive"]);
    }

    [Test]
    public async Task Apply_AddsForeignKeysRestrict()
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
            WHERE fk.parent_object_id = OBJECT_ID(N'Accounts')
            ORDER BY fk.name
            """;

        var rows = new List<(string Name, string Referenced, string Column, byte DeleteAction)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetByte(3)));
        }

        rows.ShouldBe([
            ("FK_Accounts_Currencies_Currency", "Currencies", "Code", 0),
            ("FK_Accounts_Tenants_TenantId", "Tenants", "Id", 0),
            ("FK_Accounts_Users_CustomerId", "Users", "Id", 0)
        ]);
    }

    [Test]
    public async Task Apply_AddsTypeAndStatusChecks()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT cc.name, cc.definition
            FROM sys.check_constraints cc
            WHERE cc.parent_object_id = OBJECT_ID(N'Accounts')
            ORDER BY cc.name
            """;

        var rows = new List<(string Name, string Definition)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        rows.Select(row => row.Name).ShouldContain("CK_Accounts_Type");
        rows.Select(row => row.Name).ShouldContain("CK_Accounts_Status");

        var type = rows.Single(row => row.Name == "CK_Accounts_Type");
        type.Definition.ShouldContain("[Type]", Case.Insensitive);
        type.Definition.ShouldContain("0");
        type.Definition.ShouldContain("5");

        var status = rows.Single(row => row.Name == "CK_Accounts_Status");
        status.Definition.ShouldContain("[Status]", Case.Insensitive);
        status.Definition.ShouldContain("0");
        status.Definition.ShouldContain("1");
    }

    [Test]
    public async Task Apply_IsActiveIsPersistedComputedFromOpenStatus()
    {
        await using var db = OpenDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT ty.name, c.is_nullable, cc.is_persisted, cc.definition
            FROM sys.columns c
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(N'Accounts') AND c.name = N'IsActive'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("bit");
        reader.GetBoolean(1).ShouldBeFalse();
        reader.GetBoolean(2).ShouldBeTrue();
        var definition = reader.GetString(3);
        definition.ShouldContain("[Status]", Case.Insensitive);
        definition.ShouldContain("0");
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task IsActive_FollowsStatusOnInsert()
    {
        await using var db = OpenDb();
        var (tenantA, _) = await TwoTenantFixture.InsertAsync(db);
        var (_, _, customerA, _) = await TwoTenantFixture.InsertTwoAdvisersWithCustomersAsync(db, tenantA);

        await db.Database.OpenConnectionAsync();
        await InsertAccount(db, tenantA.Id, customerA.Id, "Open bank", type: 0, status: 0);
        await InsertAccount(db, tenantA.Id, customerA.Id, "Closed other", type: 3, status: 1);

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT [Name], [IsActive] FROM [Accounts]
            WHERE [CustomerId] = @customerId
            ORDER BY [Name]
            """;
        var customer = command.CreateParameter();
        customer.ParameterName = "@customerId";
        customer.Value = customerA.Id;
        command.Parameters.Add(customer);

        var rows = new List<(string Name, bool IsActive)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetBoolean(1)));
        }

        rows.ShouldBe([
            ("Closed other", false),
            ("Open bank", true)
        ]);
    }

    [Test]
    public async Task SameType_AllowedInAnotherTenant()
    {
        await using var db = OpenDb();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (_, _, customerA, _) = await TwoTenantFixture.InsertTwoAdvisersWithCustomersAsync(db, tenantA);
        var (_, _, customerB, _) = await TwoTenantFixture.InsertTwoAdvisersWithCustomersAsync(db, tenantB);

        await db.Database.OpenConnectionAsync();
        await InsertAccount(db, tenantA.Id, customerA.Id, "Everyday", type: 0, status: 0);
        await InsertAccount(db, tenantB.Id, customerB.Id, "Everyday", type: 0, status: 0);

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [Accounts] WHERE [Name] = N'Everyday' AND [Type] = 0";
        var count = (int)(await command.ExecuteScalarAsync())!;
        count.ShouldBe(2);
    }

    private static async Task<List<string>> IndexColumns(ApplicationDbContext db, string indexName)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.object_id = OBJECT_ID(N'Accounts') AND i.name = @name
            ORDER BY ic.key_ordinal
            """;
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

    private static async Task InsertAccount(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        string name,
        int type,
        int status)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO [Accounts]
                ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
            VALUES
                (NEWID(), @tenantId, @customerId, @name, @type, @status, 'NZD', SYSDATETIMEOFFSET(), N'system')
            """;
        AddParameter(command, "@tenantId", tenantId);
        AddParameter(command, "@customerId", customerId);
        AddParameter(command, "@name", name);
        AddParameter(command, "@type", type);
        AddParameter(command, "@status", status);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static (string Name, string Type, int? Length, string Nullable, int Identity) Column(
        IReadOnlyList<(string Name, string Type, int? Length, string Nullable, int Identity)> columns,
        string name) =>
        columns.Single(column => column.Name == name);

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
