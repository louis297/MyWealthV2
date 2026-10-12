using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

public class TestSeedSmokeTests : TestBase
{
    [Test]
    public async Task DemoAdviser_SeesSeededCashAndVtiMarketValue()
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

        var response = await AccountHttp.Send(HttpMethod.Get, "/net-worth", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        var currencies = root.GetProperty("currencies").EnumerateArray().ToList();
        currencies.Select(row => row.GetProperty("currency").GetString()).ShouldBe(["NZD", "USD"]);
        AssertCurrency(currencies[0], "NZD", cash: 100m, holdings: 0m);
        AssertCurrency(currencies[1], "USD", cash: 500m, holdings: 8000m);

        var byCustomer = root.GetProperty("breakdowns").GetProperty("byCustomer").EnumerateArray().ToList();
        byCustomer.Select(row => row.GetProperty("name").GetString()).ShouldBe(["Demo Customer"]);

        var byType = root.GetProperty("breakdowns").GetProperty("byAccountType").EnumerateArray().ToList();
        byType.Select(row => row.GetProperty("type").GetString()).ShouldBe(["Bank", "Brokerage", "Cash"]);

        var top = root.GetProperty("breakdowns").GetProperty("topHoldings").EnumerateArray().Single();
        top.GetProperty("symbol").GetString().ShouldBe("VTI");
        top.GetProperty("quantity").GetDecimal().ShouldBe(32m);
        top.GetProperty("marketValue").GetDecimal().ShouldBe(8000m);
        top.GetProperty("currency").GetString().ShouldBe("USD");
    }

    private static void AssertCurrency(JsonElement row, string currency, decimal cash, decimal holdings)
    {
        row.GetProperty("currency").GetString().ShouldBe(currency);
        var assets = row.GetProperty("assets");
        assets.GetProperty("cash").GetDecimal().ShouldBe(cash);
        assets.GetProperty("holdings").GetDecimal().ShouldBe(holdings);
        assets.GetProperty("total").GetDecimal().ShouldBe(cash + holdings);
        row.GetProperty("liabilities").GetProperty("total").GetDecimal().ShouldBe(0m);
        row.GetProperty("net").GetDecimal().ShouldBe(cash + holdings);
    }
}
