using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MyWealthV2.Infrastructure.Data.Schema;

public sealed class SchemaApplicator(
    ISchemaScriptSource scripts,
    ISchemaVersionStore versions,
    ISqlBatchExecutor executor,
    ILogger<SchemaApplicator>? logger = null)
{
    private readonly ILogger<SchemaApplicator> _logger = logger ?? NullLogger<SchemaApplicator>.Instance;

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var applied = await versions.GetAppliedNamesAsync(cancellationToken);
        var pending = scripts.GetScripts().Where(script => !applied.Contains(script.Name)).ToList();

        if (pending.Count == 0)
        {
            _logger.LogInformation("Schema is up to date.");
            return;
        }

        _logger.LogInformation("Applying {Count} schema script(s).", pending.Count);

        foreach (var script in pending)
        {
            _logger.LogInformation("Applying schema script {ScriptName}.", script.Name);
            await executor.ExecuteAsync(script.Sql, cancellationToken);
            await versions.MarkAppliedAsync(script.Name, cancellationToken);
        }
    }
}
