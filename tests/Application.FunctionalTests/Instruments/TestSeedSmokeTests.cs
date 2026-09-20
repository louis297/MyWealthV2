using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

public class TestSeedSmokeTests : TestBase
{
    [Test]
    public async Task DemoTenant_HasThreeSeedSymbols()
    {
        var tenant = await TestApp.CreateTenantAsync(
            DevelopmentIdentitySeeder.DemoTenantName,
            DevelopmentIdentitySeeder.DemoTenantCode);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin,
            "Tenant Admin",
            DevelopmentIdentitySeeder.TenantAdminEmail,
            DevelopmentIdentitySeeder.TenantAdminPassword,
            tenant.Id);

        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            await TestSeed.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.TenantAdminEmail,
            DevelopmentIdentitySeeder.TenantAdminPassword,
            DevelopmentIdentitySeeder.DemoTenantCode);

        var response = await InstrumentHttp.Send(HttpMethod.Get, "/instruments", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var symbols = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("symbol").GetString())
            .ToList();
        symbols.ShouldBe(["AIA", "NZD-CASH", "VTI"]);
    }
}
