namespace MyWealthV2.Schema;

public interface ISqlBatchExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken);
}
