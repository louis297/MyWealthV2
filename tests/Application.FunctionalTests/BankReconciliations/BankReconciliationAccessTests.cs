using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;

namespace MyWealthV2.Application.FunctionalTests.BankReconciliations;

/// <summary>Bank-reconciliation R1, R17, R18 and the two-tenant rules of §5: who may run and read a reconciliation.</summary>
[Parallelizable]
public class BankReconciliationAccessTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect_OnEveryRoute()
    {
        foreach (var (method, route, body) in Routes(Guid.NewGuid(), Guid.NewGuid()))
        {
            var response = await Send(method, route, accessToken: null, body);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task AdviserCustomerAndSystemAdmin_Return403_OnEveryRoute()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var run = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        var adviser = await world.SignInAdviserAsync();
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { adviser.AccessToken, customer.AccessToken, system.AccessToken })
        {
            foreach (var (method, route, body) in Routes(run, world.ClientMoney.PublicId))
            {
                (await Send(method, route, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
            }
        }
    }

    [Test]
    public async Task ADisabledTenant_Returns403_OnEveryRoute()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var run = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        foreach (var (method, route, body) in Routes(run, world.ClientMoney.PublicId))
        {
            (await Send(method, route, world.TenantAdmin.AccessToken, body))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
        }
    }

    [Test]
    public async Task RunNow_ForAMissingClientMoneyAccount_Returns404()
    {
        var world = await DepositMatchingWorld.CreateAsync();

        (await PostAsync(world.TenantAdmin.AccessToken, new { clientMoneyAccountId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"{BankReconciliationHttp.Path}/{Guid.NewGuid()}", world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task RunNow_WithATenantIdOrAnUnknownField_Returns400_AndStoresNothing()
    {
        var world = await DepositMatchingWorld.CreateAsync();

        var tenant = await PostAsync(world.TenantAdmin.AccessToken,
            new { clientMoneyAccountId = world.ClientMoney.PublicId, tenantId = world.Tenant.PublicId });
        tenant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustodialHttp.ErrorsAsync(tenant)).ShouldContainKey("TenantId");
        (await PostAsync(world.TenantAdmin.AccessToken,
                new { clientMoneyAccountId = world.ClientMoney.PublicId, trigger = "Scheduled" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var missing = await PostAsync(world.TenantAdmin.AccessToken, new { });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustodialHttp.ErrorsAsync(missing)).ShouldContainKey("ClientMoneyAccountId");

        (await ListAsync(world.TenantAdmin.AccessToken)).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [TestCase("?tenantId=00000000-0000-0000-0000-000000000001", "TenantId")]
    [TestCase("?colour=red", "colour")]
    public async Task AReadWithATenantIdOrAnUnknownQueryField_Returns400(string query, string field)
    {
        var world = await DepositMatchingWorld.CreateAsync();

        foreach (var route in new[] { BankReconciliationHttp.Path, $"{BankReconciliationHttp.Path}/overview" })
        {
            var response = await Send(HttpMethod.Get, route + query, world.TenantAdmin.AccessToken);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, route);
            (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(field);
        }
    }

    [Test]
    public async Task RunNow_OnAnInactiveClientMoneyAccount_Returns201()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var system = await TenantHttp.SignInSystemAdmin();
        (await ClientMoneyHttp.SetActiveAsync(system.AccessToken, world.Tenant.PublicId, world.ClientMoney.PublicId, active: false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("NoBankBalance");
    }

    [Test]
    public async Task AnotherTenantsRunAndClientMoneyAccount_Return404()
    {
        var north = await DepositMatchingWorld.CreateAsync();
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var northRun = await RunAsync(north.TenantAdmin.AccessToken, north.ClientMoney.PublicId);

        (await Send(HttpMethod.Get, $"{BankReconciliationHttp.Path}/{northRun}", south.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(south.TenantAdmin.AccessToken, new { clientMoneyAccountId = north.ClientMoney.PublicId }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(south.TenantAdmin.AccessToken)).GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await ListAsync(south.TenantAdmin.AccessToken, $"?clientMoneyAccountId={north.ClientMoney.PublicId}"))
            .GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await OverviewAsync(south.TenantAdmin.AccessToken)).GetProperty("items").EnumerateArray()
            .Select(entry => entry.GetProperty("clientMoneyAccount").GetProperty("id").GetGuid())
            .ShouldBe([south.ClientMoney.PublicId]);
    }

    [Test]
    public async Task AnotherTenantsCustodialCashAndLines_InTheSameCurrencyOnTheSharedConnection_NeverAppear()
    {
        var north = await DepositMatchingWorld.CreateAsync();
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        await north.ReceiveAsync(100m, north.DepositReference);
        await south.ReceiveAsync(500m, south.DepositReference);
        await south.ReceiveAsync(60m, "Unknown ref");
        await LegAccountsFixture.InsertCashTransactionAsync(south.Tenant.Id, TransactionType.TransferIn, (south.AccountId, 40m));

        var run = await RunAndGetAsync(north.TenantAdmin.AccessToken, north.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBeEmpty();
        Decimal(run, "bankBalance").ShouldBe(100m);
        Decimal(run, "custodialCash").ShouldBe(100m);
        Decimal(run, "statementLinesTotal").ShouldBe(100m);
    }
}
