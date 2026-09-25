using MyWealthV2.Infrastructure.Data.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Schema;

public class TransactionsScriptTests
{
    [Test]
    public void SchemaDirectory_Includes0013TransactionsScript()
    {
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain("0013_transactions.sql");
    }

    private static string SchemaDirectory() => Path.Combine(AppContext.BaseDirectory, "schema");
}
