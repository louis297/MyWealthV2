using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Users.Customers;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class DevelopmentSeedSecondFirmTests : TestBase
{
    [Test]
    public async Task SecondFirm_TenantAdminAndAdviser_SignInWithTheSharedPassword()
    {
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            await DevelopmentIdentitySeeder.SeedFirmsAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SecondTenantAdminEmail,
            DevelopmentIdentitySeeder.TenantAdminPassword,
            DevelopmentIdentitySeeder.SecondTenantCode);
        admin.AccessToken.ShouldNotBeNullOrWhiteSpace();

        var adviser = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SecondAdviserEmail,
            DevelopmentIdentitySeeder.AdviserPassword,
            DevelopmentIdentitySeeder.SecondTenantCode);
        adviser.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task SecondFirm_HasTheSameCustomerBookAndGrants()
    {
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            await DevelopmentIdentitySeeder.SeedFirmsAsync(scope.ServiceProvider, CancellationToken.None);
            await TestSeed.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var adviser = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SecondAdviserEmail,
            DevelopmentIdentitySeeder.AdviserPassword,
            DevelopmentIdentitySeeder.SecondTenantCode);

        var customers = await CustomerHttp.Send(HttpMethod.Get, "/users/customers", adviser.AccessToken);
        customers.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await customers.Content.ReadAsStringAsync()))
        {
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
            items.Select(item => item.GetProperty("email").GetString())
                .ShouldBe([TestSeed.SecondCustomerEmail]);
            items.Single().GetProperty("name").GetString().ShouldBe(TestSeed.SecondCustomerName);
        }

        var accounts = await AccountHttp.Send(HttpMethod.Get, "/accounts", adviser.AccessToken);
        accounts.StatusCode.ShouldBe(HttpStatusCode.OK);
        List<string> firstAccountIds;
        using (var json = JsonDocument.Parse(await accounts.Content.ReadAsStringAsync()))
        {
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
            items.Select(item => item.GetProperty("name").GetString()).OrderBy(name => name).ShouldBe([
                "Demo brokerage",
                "Demo cash tin",
                "Demo closed other",
                "Demo everyday bank"
            ]);
            firstAccountIds = items.Select(item => item.GetProperty("id").GetString()!).OrderBy(id => id).ToList();
        }

        var instruments = await InstrumentHttp.Send(HttpMethod.Get, "/instruments", adviser.AccessToken);
        instruments.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await instruments.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("symbol").GetString())
                .ShouldBe([
                    "6758", "7203", "9984", "AAPL", "AIA", "AIR", "BHP", "CBA", "FPH", "HSBA",
                    "MEL", "MSFT", "NZD-CASH", "OR", "SAP", "SHEL", "ULVR", "VTI", "WOW"
                ]);
        }

        var reports = await TenantHttp.Send(HttpMethod.Get, "/reports", adviser.AccessToken);
        reports.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await reports.Content.ReadAsStringAsync()))
        {
            json.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("code").GetString())
                .OrderBy(code => code)
                .ShouldBe(["adviser-balances", "customer-transactions"]);
        }

        var worth = await AccountHttp.Send(HttpMethod.Get, "/net-worth", adviser.AccessToken);
        worth.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await worth.Content.ReadAsStringAsync()))
        {
            var currencies = json.RootElement.GetProperty("currencies").EnumerateArray().ToList();
            currencies.Select(row => row.GetProperty("currency").GetString()).ShouldBe(["NZD", "USD"]);
            AssertCurrency(currencies[0], "NZD", cash: 100m, holdings: 0m);
            AssertCurrency(currencies[1], "USD", cash: 500m, holdings: 8000m);
            json.RootElement.GetProperty("breakdowns").GetProperty("byCustomer").EnumerateArray()
                .Select(row => row.GetProperty("name").GetString())
                .ShouldBe([TestSeed.SecondCustomerName]);
        }

        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            await TestSeed.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var again = await AccountHttp.Send(HttpMethod.Get, "/accounts", adviser.AccessToken);
        using var againJson = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        againJson.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .OrderBy(id => id)
            .ShouldBe(firstAccountIds);
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
