using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MyWealthV2.Infrastructure.IntegrationTests;

/// <summary>
/// The host builder for tests. It has the default sources of <see cref="Host.CreateApplicationBuilder()"/>, but its
/// JSON files do not reload on change. A reloading source watches the content root, and every watcher holds one
/// inotify instance on Linux. A builder whose setup throws before <c>Build()</c> is never disposed, so its watcher
/// stayed open until the process ended. A CI runner allows 128 instances per user, so the leak stopped later hosts
/// from starting.
/// </summary>
public static class TestHost
{
    public const string ReloadConfigOnChangeKey = "hostBuilder:reloadConfigOnChange";

    public static HostApplicationBuilder CreateBuilder()
    {
        // An in-memory source: a ConfigurationManager with no source drops a value set through its indexer.
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(ReloadConfigOnChangeKey, "false")]);
        return Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Configuration = configuration });
    }
}
