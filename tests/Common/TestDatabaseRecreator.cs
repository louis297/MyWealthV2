using Microsoft.Data.SqlClient;

namespace MyWealthV2.Tests.Common;

/// <summary>
/// Drops and recreates a test database, so each run starts from an empty database even when
/// the TestAppHost SQL Server container is persistent and outlives earlier runs.
/// Linked into each test project that uses TestAppHost.
/// </summary>
internal static class TestDatabaseRecreator
{
    public static async Task RecreateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        var database = target.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("The test connection string has no database name.");
        }

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        var quoted = "[" + database.Replace("]", "]]") + "]";

        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                IF DB_ID(@name) IS NOT NULL
                BEGIN
                    ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quoted};
                END;
                CREATE DATABASE {quoted};
                """;
            command.Parameters.AddWithValue("@name", database);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        SqlConnection.ClearAllPools();
    }
}
