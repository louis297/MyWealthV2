using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Infrastructure.Data.Schema;

public sealed class EfSchemaVersionStore(ApplicationDbContext db) : ISchemaVersionStore
{
    public async Task<IReadOnlySet<string>> GetAppliedNamesAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
                SELECT CAST(NULL AS nvarchar(200)) AS ScriptName WHERE 1 = 0;
            ELSE
                SELECT [ScriptName] FROM [SchemaVersions];
            """;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async Task MarkAppliedAsync(string scriptName, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO [SchemaVersions] ([ScriptName], [AppliedAt])
            VALUES (@name, SYSUTCDATETIME());
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = scriptName;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
