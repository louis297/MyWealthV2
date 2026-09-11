namespace MyWealthV2.Infrastructure.Data.Schema;

public interface ISqlBatchExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken);
}
