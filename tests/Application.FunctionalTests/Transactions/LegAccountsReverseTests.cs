using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Leg accounts R6, R8, R10, R12: reverse covers every leg account, all or nothing.</summary>
public class LegAccountsReverseTests : TestBase
{
    [Test]
    public async Task Reverse_PutsAnOppositeLegOnEachLegAccount()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, ned: 100m, nora: 0m);
        var originalId = await LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut,
            (world.NedAccountId, -50m), (world.NoraAccountId, 50m));

        var response = await LegAccountsFixture.ReverseAsync(world.TenantAdmin.AccessToken, originalId);

        var reversalId = await LegAccountsFixture.ReadIdAsync(response);
        (await LegAccountsFixture.CashLegsAsync(reversalId)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NoraAccountId, -50m),
            (world.NedAccountId, 50m)
        ]);
        (await LegAccountsFixture.CashLegsAsync(originalId)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NedAccountId, -50m),
            (world.NoraAccountId, 50m)
        ]);
    }

    [Test]
    public async Task Reverse_AdviserAssignedToOnlyOneLegAccountIs404AndWritesNothing()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, ned: 100m, nora: 0m);
        var originalId = await LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut,
            (world.NedAccountId, -50m), (world.NoraAccountId, 50m));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        (await LegAccountsFixture.ReverseAsync(world.Ann.AccessToken, originalId))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LegAccountsFixture.ReverseAsync(world.Bob.AccessToken, originalId))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task Reverse_IsRejectedWhenAnyLegAccountIsClosed()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, ned: 100m, nora: 0m);
        var originalId = await LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut,
            (world.NedAccountId, -50m), (world.NoraAccountId, 50m));
        await LegAccountsFixture.CloseAccountDirectlyAsync(world.NoraAccountId);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        (await LegAccountsFixture.ReverseAsync(world.TenantAdmin.AccessToken, originalId))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task Reverse_IsRejectedWhenAnyLegAccountsCustomerIsDisabled()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, ned: 100m, nora: 0m);
        var originalId = await LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut,
            (world.NedAccountId, -50m), (world.NoraAccountId, 50m));
        await LegAccountsFixture.DisableCustomerAsync(world.Nora.Id);

        var response = await LegAccountsFixture.ReverseAsync(world.TenantAdmin.AccessToken, originalId);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    [Test]
    public async Task Reverse_IsRejectedWhenAnyLegAccountWouldGoNegative()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, ned: 100m, nora: 0m);
        var originalId = await LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut,
            (world.NedAccountId, -50m), (world.NoraAccountId, 50m));
        await LegAccountsFixture.InsertCashTransactionAsync(
            world.Tenant.Id, TransactionType.TransferOut, (world.NoraAccountId, -50m));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        (await LegAccountsFixture.ReverseAsync(world.TenantAdmin.AccessToken, originalId))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    private static async Task FundAsync(LegAccountsWorld world, decimal ned, decimal nora)
    {
        if (ned != 0m)
        {
            await LegAccountsFixture.InsertCashTransactionAsync(
                world.Tenant.Id, TransactionType.TransferIn, (world.NedAccountId, ned));
        }

        if (nora != 0m)
        {
            await LegAccountsFixture.InsertCashTransactionAsync(
                world.Tenant.Id, TransactionType.TransferIn, (world.NoraAccountId, nora));
        }
    }
}
