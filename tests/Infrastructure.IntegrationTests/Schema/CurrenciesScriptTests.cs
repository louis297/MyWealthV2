using MyWealthV2.Infrastructure.Data.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Schema;

public class CurrenciesScriptTests
{
    [Test]
    public void SchemaDirectory_HasNo0002CurrenciesScript()
    {
        File.Exists(Path.Combine(SchemaDirectory(), "0002_currencies.sql")).ShouldBeFalse();
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldNotContain("0002_currencies.sql");
    }

    [Test]
    public void SchemaDirectory_Includes0008CurrenciesScript()
    {
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain("0008_currencies.sql");
    }

    private static string SchemaDirectory() => Path.Combine(AppContext.BaseDirectory, "schema");
}
