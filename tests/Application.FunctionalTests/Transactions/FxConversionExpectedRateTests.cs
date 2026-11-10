using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>FX conversions R12: expectedRate must equal the stored rate; else 409 and nothing is written.</summary>
public class FxConversionExpectedRateTests : TestBase
{
    [TestCase(0.59130000)]
    [TestCase(0.59119999)]
    public async Task ExpectedRateDiffers_Returns409WithTheFieldAndWritesNothing(decimal expectedRate)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        var response = await FxConversionTests.PostAsync(
            world.Legs.TenantAdmin.AccessToken,
            FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m, expectedRate: expectedRate));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => property.Name)
            .ShouldContain(name => string.Equals(name, "expectedRate", StringComparison.OrdinalIgnoreCase));
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await FxConversionTests.CountFxDetailsAsync()).ShouldBe(0);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(100m);
    }

    [Test]
    public async Task RateChangedAfterTheQuote_Returns409ThenTheNewRateIsAccepted()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        await FxConversionTests.SetRateAsync("NZD", "USD", 0.60000000m, new DateOnly(2026, 11, 10));

        (await FxConversionTests.PostAsync(
                world.Legs.TenantAdmin.AccessToken,
                FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var id = await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            world.Legs.TenantAdmin.AccessToken,
            FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m, expectedRate: 0.60000000m)));
        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NzdAccountId, -10m),
            (world.UsdAccountId, 6.00m)
        ]);
    }
}
