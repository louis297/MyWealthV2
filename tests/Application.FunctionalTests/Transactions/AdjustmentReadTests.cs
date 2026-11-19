using System.Net;
using System.Text.Json;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Adjustments R21, R22: <c>adjustment</c> on the transaction item, the type filter, and who reads it.</summary>
[Parallelizable]
public class AdjustmentReadTests : TestBase
{
    [Test]
    public async Task Get_AnAdjustment_HasTheReason()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var id = await ReadIdAsync(await world.AdjustAsync(
            AdjustmentWorld.Body(world.AccountId, amount: 5m, reason: "ReconciliationBreak")));

        using var item = await GetTransactionAsync(world.Admin, id);

        item.RootElement.GetProperty("type").GetString().ShouldBe("Adjustment");
        item.RootElement.GetProperty("adjustment").GetProperty("reason").GetString().ShouldBe("ReconciliationBreak");
    }

    [Test]
    public async Task Get_AnotherTransaction_HasAdjustmentNull()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);
        var buy = await world.BuyAsync(1m);

        using var item = await GetTransactionAsync(world.Admin, buy);

        item.RootElement.TryGetProperty("adjustment", out var adjustment).ShouldBeTrue();
        adjustment.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task List_TypeAdjustment_ListsOnlyAdjustments_WithTheReason_AndOtherItemsHaveNull()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);
        var id = await ReadIdAsync(await world.AdjustAsync(
            AdjustmentWorld.Body(world.AccountId, amount: -5m, reason: "Other")));

        var adjustments = await ListAsync(world.Admin, "/transactions?type=Adjustment");
        var all = await ListAsync(world.Admin, $"/transactions?accountId={world.AccountId}");

        var item = adjustments.ShouldHaveSingleItem();
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("adjustment").GetProperty("reason").GetString().ShouldBe("Other");
        all.Count.ShouldBe(2);
        all.Where(row => row.GetProperty("id").GetGuid() != id)
            .ShouldAllBe(row => row.GetProperty("adjustment").ValueKind == JsonValueKind.Null);
    }

    [Test]
    public async Task Customer_ReadsTheAdjustmentOnTheirOwnAccount()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var id = await ReadIdAsync(await world.AdjustAsync(
            AdjustmentWorld.Body(world.AccountId, amount: 5m, reason: "EntryError")));

        using var item = await GetTransactionAsync(world.CustomerToken, id);

        item.RootElement.GetProperty("adjustment").GetProperty("reason").GetString().ShouldBe("EntryError");
        item.RootElement.GetProperty("memo").GetString().ShouldBe("Correction after the broker statement");
    }

    [Test]
    public async Task Adviser_ReadsTheAdjustmentOnAnAssignedCustomersAccount()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var id = await ReadIdAsync(await world.AdjustAsync(
            AdjustmentWorld.Body(world.AccountId, amount: 5m, reason: "CorporateAction")));

        using var item = await GetTransactionAsync(world.AdviserToken, id);

        item.RootElement.GetProperty("adjustment").GetProperty("reason").GetString().ShouldBe("CorporateAction");
    }

    [Test]
    public async Task OtherTenantsAdjustment_Returns404OnGet_AndIsNotListed()
    {
        var north = await AdjustmentWorld.CreateAsync("adjust-north");
        var south = await AdjustmentWorld.CreateAsync("adjust-south");
        var id = await ReadIdAsync(await south.AdjustAsync(AdjustmentWorld.Body(south.AccountId, amount: 5m)));

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", north.Admin))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(north.Admin, "/transactions?type=Adjustment")).ShouldBeEmpty();
    }

    private static async Task<List<JsonElement>> ListAsync(string accessToken, string path)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
