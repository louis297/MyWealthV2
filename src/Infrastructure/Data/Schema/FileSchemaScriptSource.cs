namespace MyWealthV2.Infrastructure.Data.Schema;

public sealed class FileSchemaScriptSource(string directory)
{
    public IReadOnlyList<SchemaScript> GetScripts()
    {
        return Directory
            .EnumerateFiles(directory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => new SchemaScript(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();
    }
}
