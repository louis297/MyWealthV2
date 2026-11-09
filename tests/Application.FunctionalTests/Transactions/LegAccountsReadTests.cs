using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Leg accounts R7 and R18: read visibility via any leg account, other legs omitted.</summary>
public class LegAccountsReadTests : TestBase
{
    [Test]
    public async Task Get_AdviserSeesOnlyTheLegsOnAssignedAccounts()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var id = await InsertTwoAccountTransactionAsync(world);

        var (item, raw) = await GetAsync(world.Ann.AccessToken, id);

        TransactionJson.CashLegs(item).Select(AccountOf).ShouldBe([world.NedAccountId]);
        raw.ShouldNotContain(world.NoraAccountId.ToString());
        raw.ShouldNotContain(world.Nora.PublicId.ToString());
    }

    [Test]
    public async Task Get_CustomerSeesOnlyTheLegsOnOwnAccounts()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var id = await InsertTwoAccountTransactionAsync(world);
        var nora = await TestApp.IssueAccessTokenAsync(world.Nora);

        var (item, raw) = await GetAsync(nora.AccessToken, id);

        TransactionJson.CashLegs(item).Select(AccountOf).ShouldBe([world.NoraAccountId]);
        raw.ShouldNotContain(world.NedAccountId.ToString());
        raw.ShouldNotContain(world.Ned.PublicId.ToString());
    }

    [Test]
    public async Task Get_TenantAdminSeesEveryLeg()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var id = await InsertTwoAccountTransactionAsync(world);

        var (item, _) = await GetAsync(world.TenantAdmin.AccessToken, id);

        TransactionJson.CashLegs(item).Select(AccountOf).ShouldBe(
            [world.NedAccountId, world.NoraAccountId], ignoreOrder: true);
    }

    [Test]
    public async Task List_ReturnsATwoAccountTransactionOnceAndCountsTransactions()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var id = await InsertTwoAccountTransactionAsync(world);

        var json = await ListAsync(world.TenantAdmin.AccessToken, "");

        var items = json.GetProperty("items").EnumerateArray().ToList();
        items.Count(item => item.GetProperty("id").GetGuid() == id).ShouldBe(1);
        json.GetProperty("totalCount").GetInt32().ShouldBe(items.Count);
        TransactionJson.CashLegs(items.Single(item => item.GetProperty("id").GetGuid() == id))
            .Count.ShouldBe(2);
    }

    [Test]
    public async Task List_AdviserGetsOnlyAssignedLegsAndAccountFilterMatchesAnyLeg()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var id = await InsertTwoAccountTransactionAsync(world);

        var ann = await ListAsync(world.Ann.AccessToken, "");
        var annItem = ann.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);
        TransactionJson.CashLegs(annItem).Select(AccountOf).ShouldBe([world.NedAccountId]);
        ann.GetRawText().ShouldNotContain(world.NoraAccountId.ToString());

        var byNora = await ListAsync(world.TenantAdmin.AccessToken, $"?accountId={world.NoraAccountId}");
        byNora.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([id]);
        var byNed = await ListAsync(world.TenantAdmin.AccessToken, $"?customerId={world.Ned.PublicId}");
        byNed.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([id]);
    }

    private static Task<Guid> InsertTwoAccountTransactionAsync(LegAccountsWorld world) =>
        LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut,
            (world.NedAccountId, -50m), (world.NoraAccountId, 50m));

    private static Guid AccountOf(JsonElement leg) => leg.GetProperty("accountId").GetGuid();

    private static async Task<(JsonElement Item, string Raw)> GetAsync(string accessToken, Guid id)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        return (json.RootElement.Clone(), raw);
    }

    private static async Task<JsonElement> ListAsync(string accessToken, string query)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions{query}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
