using MyWealthV2.Infrastructure.Data.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Schema;

public class FileSchemaScriptSourceTests
{
    [Test]
    public void ListsSqlFilesInNameOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "0003_identity.sql"), "/* 3 */");
            File.WriteAllText(Path.Combine(dir, "0001_schema_versions.sql"), "/* 1 */");

            var source = new FileSchemaScriptSource(dir);

            source.GetScripts().Select(script => script.Name)
                .ShouldBe(["0001_schema_versions.sql", "0003_identity.sql"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
