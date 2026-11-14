using System.Net;
using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Internal transfers R2, R11: one create route per type; a transfer leg counts as another transaction.</summary>
public class InternalTransferRulesTests : TestBase
{
    [Test]
    public async Task PostTransactions_WithInternalTransferType_Returns400()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions", world.TenantAdmin.AccessToken, new
        {
            accountId = world.NedAccountId,
            type = "InternalTransfer",
            amount = 10m,
            bookedAt = "2026-11-10T09:00:00Z"
        }, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task Opening_OnAnAccountThatReceivedATransfer_Returns400()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        await LegAccountsFixture.ReadIdAsync(await InternalTransferTests.PostAsync(
            world.TenantAdmin.AccessToken, InternalTransferTests.Body(world.NedAccountId, world.NoraAccountId, 10m)));

        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions", world.TenantAdmin.AccessToken, new
        {
            accountId = world.NoraAccountId,
            type = "Opening",
            amount = 50m,
            bookedAt = "2026-11-10T10:00:00Z"
        }, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetRawText().ShouldContain("Opening");
    }
}
