using MyWealthV2.Infrastructure.Data.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Schema;

public class AccountsScriptTests
{
    [Test]
    public void SchemaDirectory_Includes0012AccountsScript()
    {
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain("0012_accounts.sql");
    }

    private static string SchemaDirectory() => Path.Combine(AppContext.BaseDirectory, "schema");
}
