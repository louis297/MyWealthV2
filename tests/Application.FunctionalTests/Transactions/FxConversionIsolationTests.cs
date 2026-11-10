using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>FX conversions R6: authority on both Accounts and cross-tenant isolation (404, not 403).</summary>
public class FxConversionIsolationTests : TestBase
{
    [Test]
    public async Task AdviserNotAssignedToTheCustomer_Returns404AndWritesNothing()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        (await FxConversionTests.PostAsync(
                world.Legs.Bob.AccessToken,
                FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await FxConversionTests.CountFxDetailsAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ToAccountOfAnUnassignedCustomer_Returns404NotThe400ForTwoCustomers()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var noraUsd = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Nora.PublicId, "Nora USD", "USD");

        (await FxConversionTests.PostAsync(
                world.Legs.Ann.AccessToken,
                FxConversionTests.SellBody(world.NzdAccountId, noraUsd, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MissingAccount_Returns404()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);

        (await FxConversionTests.PostAsync(
                world.Legs.TenantAdmin.AccessToken,
                FxConversionTests.SellBody(world.NzdAccountId, Guid.NewGuid(), 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AccountInAnotherTenant_Returns404AndWritesNothing(bool otherTenantIsFrom)
    {
        var world = await FxWorld.CreateAsync();
        var south = await SouthAsync();
        await world.FundNzdAsync(100m);
        await LegAccountsFixture.InsertCashTransactionAsync(
            south.TenantId, TransactionType.TransferIn, (south.AccountId, 100m));
        var northBefore = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);
        var southBefore = await LegAccountsFixture.CountTransactionsAsync(south.TenantId);
        var body = otherTenantIsFrom
            ? FxConversionTests.SellBody(south.AccountId, world.UsdAccountId, 10m)
            : FxConversionTests.SellBody(world.NzdAccountId, south.AccountId, 10m);

        (await FxConversionTests.PostAsync(world.Legs.TenantAdmin.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var system = await AccountHttp.SignInSystemAdmin();
        var systemBody = new Dictionary<string, object?>(body) { ["tenantId"] = world.Legs.Tenant.PublicId };
        (await FxConversionTests.PostAsync(system.AccessToken, systemBody))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(northBefore);
        (await LegAccountsFixture.CountTransactionsAsync(south.TenantId)).ShouldBe(southBefore);
        (await FxConversionTests.CountFxDetailsAsync()).ShouldBe(0);
    }

    [Test]
    public async Task OtherTenantsConversionId_GetAndReverseReturn404()
    {
        var world = await FxWorld.CreateAsync();
        var south = await SouthAsync();
        await world.FundNzdAsync(100m);
        var id = await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            world.Legs.TenantAdmin.AccessToken,
            FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m)));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", south.TenantAdminToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LegAccountsFixture.ReverseAsync(south.TenantAdminToken, id))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
    }

    internal static async Task<(int TenantId, Guid AccountId, string TenantAdminToken)> SouthAsync()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory",
            "south-advisory",
            "tess@south.example",
            adviserEmail: "ann@south.example",
            customerEmail: "ned@south.example");
        var accountId = await InternalTransferTests.OpenAccountAsync(
            tokens.AccessToken, customer.PublicId, "South USD", "USD");
        return (tenant.Id, accountId, tokens.AccessToken);
    }
}
