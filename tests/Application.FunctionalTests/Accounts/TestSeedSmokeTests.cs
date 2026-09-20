using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class TestSeedSmokeTests : TestBase
{
    [Test]
    public async Task DemoTenant_HasFourSeedAccounts()
    {
        var tenant = await TestApp.CreateTenantAsync(
            DevelopmentIdentitySeeder.DemoTenantName,
            DevelopmentIdentitySeeder.DemoTenantCode);
        await TestApp.CreatePersonAsync(
            UserRole.Adviser,
            "Demo Adviser",
            DevelopmentIdentitySeeder.AdviserEmail,
            DevelopmentIdentitySeeder.AdviserPassword,
            tenant.Id);

        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            await TestSeed.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.AdviserEmail,
            DevelopmentIdentitySeeder.AdviserPassword,
            DevelopmentIdentitySeeder.DemoTenantCode);

        var response = await AccountHttp.Send(HttpMethod.Get, "/accounts", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .OrderBy(name => name)
            .ToList();
        names.ShouldBe([
            "Demo brokerage",
            "Demo cash tin",
            "Demo closed other",
            "Demo everyday bank"
        ]);
    }
}
