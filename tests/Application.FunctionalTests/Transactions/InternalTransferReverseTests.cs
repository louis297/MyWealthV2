using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Internal transfers R12: reverse puts the opposite leg on both Accounts, all or nothing.</summary>
public class InternalTransferReverseTests : TestBase
{
    [Test]
    public async Task Reverse_RestoresBothAccounts()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var transferId = await TransferAsync(world, 40m);

        var reversalId = await LegAccountsFixture.ReadIdAsync(
            await LegAccountsFixture.ReverseAsync(world.TenantAdmin.AccessToken, transferId));

        (await LegAccountsFixture.CashLegsAsync(reversalId)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NoraAccountId, -40m),
            (world.NedAccountId, 40m)
        ]);
        (await InternalTransferTests.CashBalanceAsync(world.TenantAdmin.AccessToken, world.NedAccountId)).ShouldBe(100m);
        (await InternalTransferTests.CashBalanceAsync(world.TenantAdmin.AccessToken, world.NoraAccountId)).ShouldBe(0m);

        var get = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{transferId}", world.TenantAdmin.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("reversedByTransactionId").GetGuid().ShouldBe(reversalId);
    }

    [Test]
    public async Task Reverse_AfterTheToAccountSpentTheCash_Returns400AndWritesNothing()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var transferId = await TransferAsync(world, 40m);
        await LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut, (world.NoraAccountId, -30m));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        (await LegAccountsFixture.ReverseAsync(world.TenantAdmin.AccessToken, transferId))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
        (await InternalTransferTests.CashBalanceAsync(world.TenantAdmin.AccessToken, world.NedAccountId)).ShouldBe(60m);
        (await InternalTransferTests.CashBalanceAsync(world.TenantAdmin.AccessToken, world.NoraAccountId)).ShouldBe(10m);
    }

    [Test]
    public async Task Reverse_AdviserAssignedToOnlyOneSide_Returns404()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var transferId = await TransferAsync(world, 40m);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        (await LegAccountsFixture.ReverseAsync(world.Ann.AccessToken, transferId))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task Reverse_WhenTheToCustomerIsDisabled_Returns400TargetUser()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var transferId = await TransferAsync(world, 40m);
        await LegAccountsFixture.DisableCustomerAsync(world.Nora.Id);

        var response = await LegAccountsFixture.ReverseAsync(world.TenantAdmin.AccessToken, transferId);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    private static async Task<Guid> TransferAsync(LegAccountsWorld world, decimal amount) =>
        await LegAccountsFixture.ReadIdAsync(await InternalTransferTests.PostAsync(
            world.TenantAdmin.AccessToken, InternalTransferTests.Body(world.NedAccountId, world.NoraAccountId, amount)));
}
