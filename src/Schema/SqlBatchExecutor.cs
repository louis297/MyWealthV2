using Microsoft.Data.SqlClient;

namespace MyWealthV2.Schema;

/// <summary>
/// Runs one script as one batch on the database the connection string names.
/// </summary>
public sealed class SqlBatchExecutor(string connectionString) : ISqlBatchExecutor
{
    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
