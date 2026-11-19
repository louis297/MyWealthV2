namespace MyWealthV2.Schema;

public interface ISchemaScriptSource
{
    IReadOnlyList<SchemaScript> GetScripts();
}
