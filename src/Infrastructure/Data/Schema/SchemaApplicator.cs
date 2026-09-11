namespace MyWealthV2.Infrastructure.Data.Schema;

public sealed class SchemaApplicator(
    ISchemaScriptSource scripts,
    ISchemaVersionStore versions,
    ISqlBatchExecutor executor)
{
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var applied = await versions.GetAppliedNamesAsync(cancellationToken);

        foreach (var script in scripts.GetScripts())
        {
            if (applied.Contains(script.Name))
            {
                continue;
            }

            await executor.ExecuteAsync(script.Sql, cancellationToken);
            await versions.MarkAppliedAsync(script.Name, cancellationToken);
        }
    }
}
