namespace MyWealthV2.Infrastructure.Data.Schema;

public interface ISchemaScriptSource
{
    IReadOnlyList<SchemaScript> GetScripts();
}
