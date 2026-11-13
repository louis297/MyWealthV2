using Microsoft.Data.SqlClient;
using MyWealthV2.Infrastructure.Data.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Tenant composite keys B2: the database rejects a reference to another tenant's row (script 0028).</summary>
public class TenantCompositeKeysSchemaTests
{
    private const string TenantCompositeKeysScript = "0028_tenant_composite_keys.sql";

    /// <summary>
    /// One case per tenant composite FK. <c>{p}</c> is the prefix of the tenant that owns the parent row;
    /// the child row always belongs to tenant <c>a</c>.
    /// </summary>
    private static readonly (string ForeignKey, string Parent, string Statement)[] CompositeKeys =
    [
        ("FK_Users_Users_TenantId_AdviserId", "Users", """
            INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
            VALUES (@identity, 1, 0, 0, 0, 0);
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), @a_tenant, N'New customer', N'new@a.example', 3, 1, @{p}_adviser, @identity, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """),
        ("FK_Accounts_Users_TenantId_CustomerId", "Users", """
            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_customer, N'New', 0, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Holdings_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [Holdings] ([PublicId], [TenantId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Holdings_Instruments_TenantId_InstrumentId", "Instruments", """
            INSERT INTO [Holdings] ([PublicId], [TenantId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @{p}_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Transactions_Transactions_TenantId_OriginalTransactionId", "Transactions", """
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [OriginalTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 5, SYSDATETIMEOFFSET(), @{p}_buy, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Transactions_Transactions_TenantId_ReversedByTransactionId", "Transactions", """
            UPDATE [Transactions] SET [ReversedByTransactionId] = @{p}_fx WHERE [Id] = @a_buy;
            """),
        ("FK_TransactionCashLegs_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @{p}_buy, @a_bank, 1, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionCashLegs_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_buy, @{p}_bank, 1, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionSecurityLegs_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @{p}_fx, @a_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionSecurityLegs_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_fx, @{p}_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionSecurityLegs_Instruments_TenantId_InstrumentId", "Instruments", """
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_fx, @a_brokerage, @{p}_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionFxDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionFxDetails] ([TenantId], [TransactionId], [BaseCurrency], [QuoteCurrency], [Rate], [RateAsOfDate], [RateSource])
            VALUES (@a_tenant, @{p}_buy, 'NZD', 'USD', 0.5, '2026-11-12', N'Frankfurter');
            """),
        ("FK_PortfolioLines_Portfolios_TenantId_PortfolioId", "Portfolios", """
            INSERT INTO [PortfolioLines] ([TenantId], [PortfolioId], [InstrumentId], [TargetWeight], [SortOrder])
            VALUES (@a_tenant, @{p}_portfolio, @a_instrument, 10, 1);
            """),
        ("FK_PortfolioLines_Instruments_TenantId_InstrumentId", "Instruments", """
            INSERT INTO [PortfolioLines] ([TenantId], [PortfolioId], [InstrumentId], [TargetWeight], [SortOrder])
            VALUES (@a_tenant, @a_portfolio, @{p}_instrument, 10, 1);
            """),
        ("FK_AccountPortfolioAssignments_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_brokerage, @a_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_AccountPortfolioAssignments_Portfolios_TenantId_PortfolioId", "Portfolios", """
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @{p}_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_ReportExecutions_ReportSchedules_TenantId_ScheduleId", "ReportSchedules", """
            INSERT INTO [ReportExecutions] ([PublicId], [ReportId], [ScheduleId], [TenantId], [TriggerKind], [Status], [ParametersJson], [Created], [CreatedBy])
            VALUES (NEWID(), @report, @{p}_schedule, @a_tenant, N'Scheduled', N'Queued', N'{}', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_ReportExecutions_Users_TenantId_TriggeredByUserId", "Users", """
            INSERT INTO [ReportExecutions] ([PublicId], [ReportId], [TenantId], [TriggerKind], [Status], [ParametersJson], [TriggeredByUserId], [Created], [CreatedBy])
            VALUES (NEWID(), @report, @a_tenant, N'Manual', N'Queued', N'{}', @{p}_adviser, SYSDATETIMEOFFSET(), N'test');
            """)
    ];

    private static readonly string[] ReplacedForeignKeys =
    [
        "FK_Users_Users_AdviserId",
        "FK_Accounts_Users_CustomerId",
        "FK_Holdings_Accounts_AccountId",
        "FK_Holdings_Instruments_InstrumentId",
        "FK_Transactions_Transactions_OriginalTransactionId",
        "FK_Transactions_Transactions_ReversedByTransactionId",
        "FK_TransactionCashLegs_Transactions_TransactionId",
        "FK_TransactionCashLegs_Accounts_AccountId",
        "FK_TransactionSecurityLegs_Transactions_TransactionId",
        "FK_TransactionSecurityLegs_Accounts_AccountId",
        "FK_TransactionSecurityLegs_Instruments_InstrumentId",
        "FK_TransactionFxDetails_Transactions_TransactionId",
        "FK_PortfolioLines_Portfolios_PortfolioId",
        "FK_PortfolioLines_Instruments_InstrumentId",
        "FK_AccountPortfolioAssignments_Accounts_AccountId",
        "FK_AccountPortfolioAssignments_Portfolios_PortfolioId",
        "FK_ReportExecutions_ReportSchedules_ScheduleId",
        "FK_ReportExecutions_Users_TriggeredByUserId"
    ];

    private static readonly string[] Parents =
        ["Users", "Accounts", "Instruments", "Transactions", "Portfolios", "ReportSchedules"];

    private static IEnumerable<TestCaseData> CompositeKeyCases() =>
        CompositeKeys.Select(key => new TestCaseData(key.ForeignKey).SetName($"Insert_ChildPointingAtOtherTenant_IsRejected({key.ForeignKey})"));

    private static IEnumerable<TestCaseData> SameTenantCases() =>
        CompositeKeys.Select(key => new TestCaseData(key.ForeignKey).SetName($"Insert_ChildPointingAtSameTenant_IsAccepted({key.ForeignKey})"));

    [Test]
    public void SchemaDirectory_Includes0028TenantCompositeKeysScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(TenantCompositeKeysScript);
    }

    [TestCaseSource(nameof(CompositeKeyCases))]
    public async Task Insert_ChildPointingAtOtherTenant_IsRejected(string foreignKey)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunInRolledBackTransaction(foreignKey, "b"));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(foreignKey);
    }

    [TestCaseSource(nameof(SameTenantCases))]
    public async Task Insert_ChildPointingAtSameTenant_IsAccepted(string foreignKey)
    {
        await RunInRolledBackTransaction(foreignKey, "a");
    }

    [Test]
    public async Task Apply_EveryCompositeFkHasTheExpectedColumnsParentAndRestrict()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT fk.[name], OBJECT_NAME(fk.[referenced_object_id]), fk.[delete_referential_action],
                   STRING_AGG(COL_NAME(fkc.[parent_object_id], fkc.[parent_column_id]), N',')
                       WITHIN GROUP (ORDER BY fkc.[constraint_column_id]),
                   STRING_AGG(COL_NAME(fkc.[referenced_object_id], fkc.[referenced_column_id]), N',')
                       WITHIN GROUP (ORDER BY fkc.[constraint_column_id])
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.[constraint_object_id] = fk.[object_id]
            WHERE fk.[name] LIKE N'FK[_]%[_]TenantId[_]%'
            GROUP BY fk.[name], fk.[referenced_object_id], fk.[delete_referential_action]
            ORDER BY fk.[name]
            """);

        var expected = CompositeKeys
            .OrderBy(key => key.ForeignKey, StringComparer.Ordinal)
            .Select(key => (key.ForeignKey, key.Parent, (byte)0, ChildColumns(key.ForeignKey), "TenantId,Id"))
            .ToList();
        rows.Select(row => ((string)row[0], (string)row[1], (byte)row[2], (string)row[3], (string)row[4]))
            .ShouldBe(expected);
    }

    [Test]
    public async Task Apply_EveryParentHasAUniqueTenantIdIdKey()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT kc.[name],
                   STRING_AGG(COL_NAME(ic.[object_id], ic.[column_id]), N',') WITHIN GROUP (ORDER BY ic.[key_ordinal])
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.[object_id] = kc.[parent_object_id] AND ic.[index_id] = kc.[unique_index_id]
            WHERE kc.[type] = 'UQ' AND kc.[name] LIKE N'UQ[_]%[_]TenantId[_]Id'
            GROUP BY kc.[name]
            ORDER BY kc.[name]
            """);

        rows.Select(row => ((string)row[0], (string)row[1]))
            .ShouldBe(Parents
                .Select(parent => ($"UQ_{parent}_TenantId_Id", "TenantId,Id"))
                .OrderBy(key => key.Item1, StringComparer.Ordinal));
    }

    [Test]
    public async Task Apply_NoReplacedSingleColumnFkRemains()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString,
            "SELECT [name] FROM sys.foreign_keys ORDER BY [name]");

        rows.Select(row => (string)row[0]).ShouldNotContain(name => ReplacedForeignKeys.Contains(name));
    }

    [Test]
    public async Task Apply0028_WithACrossTenantRow_ThrowsAndChangesNothing()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(TenantCompositeKeysScript);

        await using var db = await ScratchSchemaDatabase.CreateAsync("TenantCompositeKeysPrecheck");
        await db.ApplyBeforeAsync(TenantCompositeKeysScript);
        await db.ExecuteAsync(
            ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
            + ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])
            + """
              INSERT INTO [Holdings] ([PublicId], [TenantId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
              VALUES (NEWID(), @a_tenant, @b_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
              """);

        var exception = await Should.ThrowAsync<SqlException>(db.ApplyAllAsync);

        exception.Message.ShouldContain("FK_Holdings_Accounts_TenantId_AccountId=1");
        var foreignKeys = (await db.ReadAsync("SELECT [name] FROM sys.foreign_keys"))
            .Select(row => (string)row[0])
            .ToList();
        foreignKeys.ShouldContain("FK_Holdings_Accounts_AccountId");
        foreignKeys.ShouldNotContain(name => name.Contains("_TenantId_"));
        (await db.ReadAsync("SELECT [name] FROM sys.key_constraints WHERE [name] LIKE N'UQ[_]%[_]TenantId[_]Id'"))
            .ShouldBeEmpty();
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{TenantCompositeKeysScript}'"))
            .ShouldBeEmpty();
    }

    private static string ChildColumns(string foreignKey) =>
        "TenantId," + foreignKey[(foreignKey.LastIndexOf("_TenantId_", StringComparison.Ordinal) + "_TenantId_".Length)..];

    private static async Task RunInRolledBackTransaction(string foreignKey, string parentPrefix)
    {
        var statement = CompositeKeys.Single(key => key.ForeignKey == foreignKey).Statement
            .Replace("{p}", parentPrefix, StringComparison.Ordinal);

        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
                + ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])
                + $"""
                   DECLARE @identity nvarchar(450) = CONVERT(nvarchar(450), NEWID());
                   DECLARE @report int = (SELECT [Id] FROM [Reports] WHERE [Code] = N'adviser-balances');
                   {statement}
                   """;
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
