using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>FX conversions R2, R16: one create route per type; a conversion leg counts as another transaction.</summary>
public class FxConversionRulesTests : TestBase
{
    [Test]
    public async Task PostTransactions_WithFxConversionType_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions", world.Legs.TenantAdmin.AccessToken, new
        {
            accountId = world.NzdAccountId,
            type = "FxConversion",
            amount = -10m,
            bookedAt = FxConversionTests.BookedAt
        }, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Opening_OnEitherAccountAfterAConversion_Returns400(bool onFrom)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            world.Legs.TenantAdmin.AccessToken,
            FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m)));

        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions", world.Legs.TenantAdmin.AccessToken, new
        {
            accountId = onFrom ? world.NzdAccountId : world.UsdAccountId,
            type = "Opening",
            amount = 50m,
            bookedAt = "2026-11-10T10:00:00+13:00"
        }, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetRawText().ShouldContain("Opening");
    }
}
