using System.Data;
using Microsoft.Data.SqlClient;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>
/// A named lock in SQL Server (<c>sp_getapplock</c>, session owner, no wait), held on its own connection while a
/// run goes on, so a second run of the same job on any worker ends at once (deposit-matching R6).
/// </summary>
public sealed class SqlRunLock(string connectionString)
{
    /// <summary>The held lock, or null when another run holds it. Dispose to release.</summary>
    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Resource", name);
        command.Parameters.AddWithValue("@LockMode", "Exclusive");
        command.Parameters.AddWithValue("@LockOwner", "Session");
        command.Parameters.AddWithValue("@LockTimeout", 0);
        var result = command.Parameters.Add("@Result", SqlDbType.Int);
        result.Direction = ParameterDirection.ReturnValue;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if ((int)result.Value < 0)
        {
            await connection.DisposeAsync();
            return null;
        }

        return new Held(connection, name);
    }

    private sealed class Held(SqlConnection connection, string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "sp_releaseapplock";
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@Resource", name);
                command.Parameters.AddWithValue("@LockOwner", "Session");
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                // Closing the session releases the lock as well.
                await connection.DisposeAsync();
            }
        }
    }
}
