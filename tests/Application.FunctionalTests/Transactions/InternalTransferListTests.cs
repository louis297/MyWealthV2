using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Internal transfers R14, R15: list type filter and per-caller legs.</summary>
public class InternalTransferListTests : TestBase
{
    [Test]
    public async Task List_TypeFilter_ReturnsOnlyThatType()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var transferId = await TransferAsync(world, world.TenantAdmin.AccessToken, 10m);

        (await ListIdsAsync(world.TenantAdmin.AccessToken, "/transactions?type=InternalTransfer"))
            .ShouldBe([transferId]);
        (await ListIdsAsync(world.TenantAdmin.AccessToken, $"/transactions?type=InternalTransfer&accountId={world.NoraAccountId}"))
            .ShouldBe([transferId]);
        (await ListIdsAsync(world.TenantAdmin.AccessToken, $"/transactions?type=TransferIn&accountId={world.NoraAccountId}"))
            .ShouldBeEmpty();
        (await ListIdsAsync(world.TenantAdmin.AccessToken, $"/transactions?type=TransferIn&accountId={world.NedAccountId}"))
            .Count.ShouldBe(1);
    }

    [Test]
    public async Task List_TypeFilter_AppliesAfterTheVisibilityScope()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        await InternalTransferTests.FundAsync(world, world.NoraAccountId, 100m);
        var nedToNora = await TransferAsync(world, world.TenantAdmin.AccessToken, 10m);
        var nell = await TestApp.CreatePersonAsync(
            Domain.Enums.UserRole.Customer, "Nell North", "nell@north.example", "Password1!",
            world.Tenant.Id, world.Nora.AdviserId);
        var nellAccount = await InternalTransferTests.OpenAccountAsync(world.TenantAdmin.AccessToken, nell.PublicId, "Nell bank");
        await LegAccountsFixture.ReadIdAsync(await InternalTransferTests.PostAsync(
            world.TenantAdmin.AccessToken, InternalTransferTests.Body(world.NoraAccountId, nellAccount, 5m)));

        (await ListIdsAsync(world.Ann.AccessToken, "/transactions?type=InternalTransfer"))
            .ShouldBe([nedToNora]);
    }

    [TestCase("Transfer")]
    [TestCase("internaltransfer")]
    [TestCase("15")]
    public async Task List_UnknownType_Returns400(string type)
    {
        var world = await LegAccountsFixture.CreateAsync();

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions?type={type}", world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CallerWhoSeesOneSide_GetsOneLegOnly()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var transferId = await TransferAsync(world, world.TenantAdmin.AccessToken, 30m);

        var get = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{transferId}", world.Bob.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var item = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var leg = TransactionJson.SingleCash(item.RootElement);
        leg.GetProperty("accountId").GetGuid().ShouldBe(world.NoraAccountId);
        leg.GetProperty("amount").GetDecimal().ShouldBe(30m);
        item.RootElement.GetRawText().ShouldNotContain(world.NedAccountId.ToString());
        item.RootElement.GetRawText().ShouldNotContain(world.Ned.PublicId.ToString());

        var list = await TransactionHttp.Send(HttpMethod.Get, "/transactions?type=InternalTransfer", world.Bob.AccessToken);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var listed = listJson.RootElement.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        TransactionJson.SingleCash(listed).GetProperty("amount").GetDecimal().ShouldBe(30m);
    }

    private static async Task<Guid> TransferAsync(LegAccountsWorld world, string accessToken, decimal amount) =>
        await LegAccountsFixture.ReadIdAsync(await InternalTransferTests.PostAsync(
            accessToken, InternalTransferTests.Body(world.NedAccountId, world.NoraAccountId, amount)));

    private static async Task<List<Guid>> ListIdsAsync(string accessToken, string path)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }
}
