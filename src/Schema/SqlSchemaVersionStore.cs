using Microsoft.Data.SqlClient;

namespace MyWealthV2.Schema;

/// <summary>
/// Reads and writes the <c>SchemaVersions</c> table of the database the connection string names.
/// Each call opens its own connection.
/// </summary>
public sealed class SqlSchemaVersionStore(string connectionString) : ISchemaVersionStore
{
    public async Task<IReadOnlySet<string>> GetAppliedNamesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
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
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [SchemaVersions] ([ScriptName], [AppliedAt])
            VALUES (@name, SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@name", scriptName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
