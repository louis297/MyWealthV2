extern alias IdentityApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.FunctionalTests.Hosting;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public class IdentityFactory(
    string connectionString,
    IReadOnlyDictionary<string, string>? extraSettings = null,
    ILoggerProvider? logs = null,
    string environment = "Development") : WebApplicationFactory<IdentityApp::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{MyWealthV2.Shared.Services.Database}", connectionString);
        builder.UseEnvironment(environment);
        builder.UseSetting("Identity:AdviserPortalClientSecret", AdviserPortalTestSecret.Value);
        builder.UseSetting("Identity:BackOfficeClientSecret", BackOfficeTestSecret.Value);
        builder.UseSetting("Identity:CustomerPortalClientSecret", CustomerPortalTestSecret.Value);
        builder.ConfigureTestServices(services =>
        {
            services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1);
            services.AddTransient<IStartupFilter, TestConnection>();
        });
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
