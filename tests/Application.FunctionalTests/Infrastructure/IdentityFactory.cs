extern alias IdentityApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public class IdentityFactory(
    string connectionString,
    IReadOnlyDictionary<string, string>? extraSettings = null,
    ILoggerProvider? logs = null) : WebApplicationFactory<IdentityApp::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{MyWealthV2.Shared.Services.Database}", connectionString);
        builder.UseEnvironment("Development");
        builder.UseSetting("Identity:AdviserPortalClientSecret", AdviserPortalTestSecret.Value);
        if (logs is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }

        if (extraSettings is null)
        {
            return;
        }

        foreach (var (key, value) in extraSettings)
        {
            builder.UseSetting(key, value);
        }
    }
}
