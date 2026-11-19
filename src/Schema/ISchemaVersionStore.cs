namespace MyWealthV2.Schema;

public interface ISchemaVersionStore
{
    Task<IReadOnlySet<string>> GetAppliedNamesAsync(CancellationToken cancellationToken);

    Task MarkAppliedAsync(string scriptName, CancellationToken cancellationToken);
}
