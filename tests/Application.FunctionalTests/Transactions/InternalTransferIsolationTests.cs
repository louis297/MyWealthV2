using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Internal transfers R6: authority on both Accounts and cross-tenant isolation (404, not 403).</summary>
public class InternalTransferIsolationTests : TestBase
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task AdviserUnassignedOnEitherSide_Returns404AndWritesNothing(bool unassignedFrom)
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        await InternalTransferTests.FundAsync(world, world.NoraAccountId, 100m);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);
        var body = unassignedFrom
            ? InternalTransferTests.Body(world.NoraAccountId, world.NedAccountId, 10m)
            : InternalTransferTests.Body(world.NedAccountId, world.NoraAccountId, 10m);

        (await InternalTransferTests.PostAsync(world.Ann.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task MissingAccount_Returns404()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);

        (await InternalTransferTests.PostAsync(
                world.TenantAdmin.AccessToken, InternalTransferTests.Body(world.NedAccountId, Guid.NewGuid(), 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AccountInAnotherTenant_Returns404AndWritesNothing(bool otherTenantIsFrom)
    {
        var world = await LegAccountsFixture.CreateAsync();
        var south = await SouthAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        await LegAccountsFixture.InsertCashTransactionAsync(
            south.TenantId, Domain.Enums.TransactionType.TransferIn, (south.AccountId, 100m));
        var northBefore = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);
        var southBefore = await LegAccountsFixture.CountTransactionsAsync(south.TenantId);
        var body = otherTenantIsFrom
            ? InternalTransferTests.Body(south.AccountId, world.NedAccountId, 10m)
            : InternalTransferTests.Body(world.NedAccountId, south.AccountId, 10m);

        (await InternalTransferTests.PostAsync(world.TenantAdmin.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var system = await AccountHttp.SignInSystemAdmin();
        var systemBody = new Dictionary<string, object?>(body) { ["tenantId"] = world.Tenant.PublicId };
        (await InternalTransferTests.PostAsync(system.AccessToken, systemBody))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(northBefore);
        (await LegAccountsFixture.CountTransactionsAsync(south.TenantId)).ShouldBe(southBefore);
    }

    [Test]
    public async Task OtherTenantsTransferId_GetAndReverseReturn404()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var south = await SouthAsync();
        await InternalTransferTests.FundAsync(world, world.NedAccountId, 100m);
        var id = await LegAccountsFixture.ReadIdAsync(await InternalTransferTests.PostAsync(
            world.TenantAdmin.AccessToken, InternalTransferTests.Body(world.NedAccountId, world.NoraAccountId, 10m)));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", south.TenantAdminToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LegAccountsFixture.ReverseAsync(south.TenantAdminToken, id))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    private static async Task<(int TenantId, Guid AccountId, string TenantAdminToken)> SouthAsync()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory",
            "south-advisory",
            "tess@south.example",
            adviserEmail: "ann@south.example",
            customerEmail: "ned@south.example");
        var accountId = await InternalTransferTests.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "South bank");
        return (tenant.Id, accountId, tokens.AccessToken);
    }
}
