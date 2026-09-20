using MyWealthV2.Infrastructure.Data.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Schema;

public class InstrumentsScriptTests
{
    [Test]
    public void SchemaDirectory_Includes0010InstrumentsScript()
    {
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain("0010_instruments.sql");
    }

    private static string SchemaDirectory() => Path.Combine(AppContext.BaseDirectory, "schema");
}
