using MyWealthV2.Infrastructure.Data.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Schema;

public class RenameIsEnabledScriptTests
{
    [Test]
    public void SchemaDirectory_Includes0011RenameScript()
    {
        new FileSchemaScriptSource(SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain("0011_rename_is_enabled_to_is_active.sql");
    }

    [Test]
    public void ShippedCatalogScripts_StillContainIsEnabled()
    {
        File.ReadAllText(Path.Combine(SchemaDirectory(), "0005_tenants.sql")).ShouldContain("[IsEnabled]");
        File.ReadAllText(Path.Combine(SchemaDirectory(), "0008_currencies.sql")).ShouldContain("[IsEnabled]");
        File.ReadAllText(Path.Combine(SchemaDirectory(), "0010_instruments.sql")).ShouldContain("[IsEnabled]");
        File.ReadAllText(Path.Combine(SchemaDirectory(), "0006_users.sql")).ShouldNotContain("[IsActive]");
    }

    private static string SchemaDirectory() => Path.Combine(AppContext.BaseDirectory, "schema");
}
