using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>FX conversions R21–R23: item `fx`, both legs for a caller who sees the Customer, list type filter.</summary>
public class FxConversionReadTests : TestBase
{
    [Test]
    public async Task Get_ReturnsBothLegsAndTheFxObject()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);
        var id = await ConvertBuyAsync(world, 500.00m);

        var item = await GetAsync(world.Legs.Ann.AccessToken, id);

        item.GetProperty("type").GetString().ShouldBe("FxConversion");
        TransactionJson.CashLegs(item)
            .Select(leg => (leg.GetProperty("accountId").GetGuid(), leg.GetProperty("amount").GetDecimal(), leg.GetProperty("currency").GetString()))
            .ShouldBe([(world.NzdAccountId, -845.74m, "NZD"), (world.UsdAccountId, 500.00m, "USD")], ignoreOrder: true);
        AssertFx(item);
    }

    [Test]
    public async Task Get_OtherTypes_HaveFxNull()
    {
        var world = await FxWorld.CreateAsync();
        var fundingId = await LegAccountsFixture.InsertCashTransactionAsync(
            world.Legs.Tenant.Id, Domain.Enums.TransactionType.TransferIn, (world.NzdAccountId, 100m));

        var item = await GetAsync(world.Legs.TenantAdmin.AccessToken, fundingId);

        item.TryGetProperty("fx", out var fx).ShouldBeTrue();
        fx.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Get_Reversal_CarriesTheCopiedFx()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);
        var id = await ConvertBuyAsync(world, 500.00m);
        var reversalId = await LegAccountsFixture.ReadIdAsync(
            await LegAccountsFixture.ReverseAsync(world.Legs.TenantAdmin.AccessToken, id));

        AssertFx(await GetAsync(world.Legs.TenantAdmin.AccessToken, reversalId));
    }

    [Test]
    public async Task Customer_ReadsOwnConversionWithBothLegsAndFx()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);
        var id = await ConvertBuyAsync(world, 500.00m);
        var ned = await TestApp.IssueAccessTokenAsync(world.Legs.Ned);

        var item = await GetAsync(ned.AccessToken, id);
        TransactionJson.CashLegs(item).Count.ShouldBe(2);
        AssertFx(item);

        var list = await ListAsync(ned.AccessToken, $"/transactions?accountId={world.UsdAccountId}");
        var listed = list.Single(row => row.GetProperty("id").GetGuid() == id);
        AssertFx(listed);

        var nora = await TestApp.IssueAccessTokenAsync(world.Legs.Nora);
        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", nora.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task List_TypeFilter_ReturnsConversionsWithFx()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);
        var id = await ConvertBuyAsync(world, 500.00m);

        var items = await ListAsync(world.Legs.TenantAdmin.AccessToken, "/transactions?type=FxConversion");

        items.Select(item => item.GetProperty("id").GetGuid()).ShouldBe([id]);
        AssertFx(items.Single());
        (await ListAsync(world.Legs.TenantAdmin.AccessToken, $"/transactions?type=FxConversion&accountId={world.UsdAccountId}"))
            .Select(item => item.GetProperty("id").GetGuid()).ShouldBe([id]);
    }

    private static void AssertFx(JsonElement item)
    {
        var fx = item.GetProperty("fx");
        fx.ValueKind.ShouldBe(JsonValueKind.Object);
        fx.GetProperty("baseCurrency").GetString().ShouldBe("NZD");
        fx.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        fx.GetProperty("rate").GetDecimal().ShouldBe(FxConversionTests.NzdUsd);
        fx.GetProperty("rateAsOfDate").GetString().ShouldBe("2026-11-09");
        fx.TryGetProperty("rateSource", out _).ShouldBeFalse();
    }

    private static async Task<Guid> ConvertBuyAsync(FxWorld world, decimal buyAmount) =>
        await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            world.Legs.TenantAdmin.AccessToken,
            FxConversionTests.BuyBody(world.NzdAccountId, world.UsdAccountId, buyAmount)));

    private static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> ListAsync(string accessToken, string path)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
