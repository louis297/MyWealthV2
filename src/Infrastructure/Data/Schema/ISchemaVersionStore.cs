namespace MyWealthV2.Infrastructure.Data.Schema;

public interface ISchemaVersionStore
{
    Task<IReadOnlySet<string>> GetAppliedNamesAsync(CancellationToken cancellationToken);

    Task MarkAppliedAsync(string scriptName, CancellationToken cancellationToken);
}
