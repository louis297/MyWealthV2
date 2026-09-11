using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Infrastructure.Data.Schema;

public sealed class EfSqlBatchExecutor(ApplicationDbContext db) : ISqlBatchExecutor
{
    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
