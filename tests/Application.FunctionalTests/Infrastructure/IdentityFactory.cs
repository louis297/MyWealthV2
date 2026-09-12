extern alias IdentityApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public class IdentityFactory(string connectionString) : WebApplicationFactory<IdentityApp::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{MyWealthV2.Shared.Services.Database}", connectionString);
        builder.UseEnvironment("Development");
    }
}
