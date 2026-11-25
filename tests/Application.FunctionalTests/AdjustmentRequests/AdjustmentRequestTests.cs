using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Transactions;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.AdjustmentRequests;

/// <summary>Adjustment review R1–R6, R19, R20: the permission, the request and the reads of an adjustment request.</summary>
[Parallelizable]
public class AdjustmentRequestTests : TestBase
{
    private const string ExternalMessage = "An adjustment can be entered only on a Custodial account.";
    private const string ClosingMessage = "This account is being closed and accepts no new activity.";

    private static IEnumerable<(HttpMethod Method, string Route)> Routes(Guid id)
    {
        yield return (HttpMethod.Get, AdjustmentRequestHttp.Path);
        yield return (HttpMethod.Get, $"{AdjustmentRequestHttp.Path}/{id}");
        yield return (HttpMethod.Post, AdjustmentRequestHttp.Path);
        yield return (HttpMethod.Post, $"{AdjustmentRequestHttp.Path}/{id}/approve");
        yield return (HttpMethod.Post, $"{AdjustmentRequestHttp.Path}/{id}/reject");
        yield return (HttpMethod.Post, $"{AdjustmentRequestHttp.Path}/{id}/cancel");
    }

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect_OnEveryRoute()
    {
        foreach (var (method, route) in Routes(Guid.NewGuid()))
        {
            var response = await AdjustmentRequestHttp.Send(method, route, accessToken: null,
                new { rowVersion = AdjustmentRequestHttp.StaleRowVersion, note = "Note" });

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task AdviserCustomerAndSystemAdmin_Return403_OnEveryRoute_EvenForAnUnknownId()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { world.Adviser, world.CustomerToken, system.AccessToken })
        {
            foreach (var target in new[] { id, Guid.NewGuid() })
            {
                foreach (var (method, route) in Routes(target))
                {
                    var body = route == AdjustmentRequestHttp.Path && method == HttpMethod.Post
                        ? (object)world.Body(amount: 5m)
                        : new { rowVersion = AdjustmentRequestHttp.StaleRowVersion, note = "Note" };
                    var response = await TransactionHttp.Send(method, route, token, body, Guid.NewGuid());

                    response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
                }
            }
        }

        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(1);
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(Domain.Enums.AdjustmentRequestStatus.Requested);
    }

    [Test]
    public async Task TenantAdmin_Requests_Returns201Requested_WithLocation_AndTheItem_AndChangesNothingInTheLedger()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await world.BuyAsync(10m);
        var cashBefore = await world.CashAsync();
        var holdingBefore = await world.HoldingAsync();
        var transactionsBefore = await TransactionCountAsync(world.AccountId);

        var response = await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, new Dictionary<string, object?>
        {
            ["accountId"] = world.AccountId,
            ["reason"] = "EntryError",
            ["memo"] = "  Buy of 2026-11-18 was filled at 25.40, not 25.10 ",
            ["reference"] = " CN-77 ",
            ["amount"] = -0.60m,
            ["security"] = world.Holding(0m, 0.60m)
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = await ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"/adjustment-requests/{id}");
        var item = await AdjustmentRequestHttp.GetAsync(world.SecondTenantAdmin, id);
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        item.GetProperty("reason").GetString().ShouldBe("EntryError");
        item.GetProperty("memo").GetString().ShouldBe("Buy of 2026-11-18 was filled at 25.40, not 25.10");
        item.GetProperty("reference").GetString().ShouldBe("CN-77");
        item.GetProperty("amount").GetDecimal().ShouldBe(-0.60m);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        var security = item.GetProperty("security");
        security.GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(world.InstrumentId);
        security.GetProperty("instrument").GetProperty("symbol").GetString().ShouldBe("VTI");
        security.GetProperty("instrument").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        security.GetProperty("quantityChange").GetDecimal().ShouldBe(0m);
        security.GetProperty("costChange").GetDecimal().ShouldBe(0.60m);
        security.GetProperty("costCurrency").GetString().ShouldBe("USD");
        item.GetProperty("status").GetString().ShouldBe("Requested");
        item.GetProperty("requestedBy").GetProperty("id").GetGuid().ShouldBe(world.Tess.PublicId);
        item.GetProperty("requestedBy").GetProperty("name").GetString().ShouldBe(world.Tess.Name);
        item.GetProperty("requestedAt").GetDateTimeOffset().ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        item.GetProperty("decision").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();

        (await world.CashAsync()).ShouldBe(cashBefore);
        (await world.HoldingAsync()).ShouldBe(holdingBefore);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
    }

    [Test]
    public async Task ACashOnlyRequest_HasSecurityNull_AndAHoldingOnlyRequest_HasAmountNull()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();

        var cash = await AdjustmentRequestHttp.GetAsync(world.TenantAdmin,
            await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m)));
        var holding = await AdjustmentRequestHttp.GetAsync(world.TenantAdmin,
            await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(security: world.Holding(10m, 250m))));

        cash.GetProperty("security").ValueKind.ShouldBe(JsonValueKind.Null);
        cash.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        holding.GetProperty("amount").ValueKind.ShouldBe(JsonValueKind.Null);
        holding.GetProperty("currency").GetString().ShouldBe("USD");
        (await world.HoldingAsync()).ShouldBeNull();
    }

    [Test]
    public async Task AnOpenRequest_ReservesNoCash()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();

        await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: -900m));

        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
        // A second request for the same cash is checked against the ledger alone (R5).
        (await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: -900m))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AnOpenRequest_ReservesNoQuantity()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await world.BuyAsync(10m);

        await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(security: world.Holding(-10m, -250m)));

        (await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(security: world.Holding(-10m, -250m))))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await world.HoldingAsync()).ShouldBe((10m, 250m));
    }

    [Test]
    public async Task AnExternalAccount_Returns400AtAccountId_AndStoresNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var key = Guid.NewGuid();

        var response = await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, AdjustmentWorld.Body(world.ExternalAccountId, amount: 5m), key);

        (await ErrorsAsync(response))["AccountId"].ShouldBe([ExternalMessage]);
        (await AdjustmentRequestHttp.CountAsync(world.ExternalAccountId)).ShouldBe(0);
        (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
    }

    [Test]
    public async Task AClosingAccount_Returns400_AndStoresNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.Withdrawals.NominatedBankAccountId);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: 5m)));

        errors[string.Empty].ShouldBe([ClosingMessage]);
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task AClosedAccount_Returns400_AndStoresNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await OrderHttp.SetAccountStatusAsync(world.AccountId, Domain.Enums.AccountStatus.Closed);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: 5m)));

        errors.Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task CashBelowZero_Returns400_AndStoresNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();

        var response = await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: -1000.01m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task CashBelowTheAvailableCash_Returns400WithTheAvailableCashMessage_AndStoresNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await RequestWithdrawalAsync(world.AccountId, 300m);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: -700.01m)));

        errors.Values.SelectMany(messages => messages)
            .ShouldContain("Available cash cannot become negative: 300.00 USD is reserved for withdrawals and orders.");
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(0);
        (await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: -700m))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [TestCase(-11, 0)]
    [TestCase(0, -251)]
    [TestCase(-10, 0)]
    public async Task AHoldingResultRefused_Returns400AtSecurity_AndStoresNothing(decimal quantityChange, decimal costChange)
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await world.BuyAsync(10m);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin,
            world.Body(security: world.Holding(quantityChange, costChange))));

        errors["Security"].ShouldBe([Domain.Entities.Transaction.AdjustmentHoldingMessage]);
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task ShapeRefusals_Return400AtTheirField_AndStoreNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var noMemo = world.Body(amount: 5m);
        noMemo["memo"] = "  ";
        var bookedAt = world.Body(amount: 5m);
        bookedAt["bookedAt"] = "2026-10-11T10:00:00Z";
        var tenant = world.Body(amount: 5m);
        tenant["tenantId"] = world.Tenant.PublicId;

        (await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body()))).Keys.ShouldContain(string.Empty);
        (await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: 0m)))).Keys.ShouldContain("Amount");
        (await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: 5m, reason: "Rounding"))))
            .Keys.ShouldContain("Reason");
        (await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, noMemo))).Keys.ShouldContain("Memo");
        (await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, bookedAt))).Keys.ShouldContain("BookedAt");
        (await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, tenant))).Keys.ShouldContain("TenantId");
        (await ErrorsAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(security: world.Holding(0m, 0m)))))
            .Keys.ShouldContain("Security");
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task UnknownAccountOrInstrument_Returns404()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();

        (await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, AdjustmentWorld.Body(Guid.NewGuid(), amount: 5m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdjustmentRequestHttp.PostAsync(world.TenantAdmin,
                world.Body(security: AdjustmentWorld.Holding(Guid.NewGuid(), 1m, 25m))))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task Idempotency_MissingKeyIs400_TheSameKeyReplays_AndAnotherBodyIs409()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var key = Guid.NewGuid();

        var noKey = await TransactionHttp.Send(HttpMethod.Post, AdjustmentRequestHttp.Path, world.TenantAdmin, world.Body(amount: 5m));
        var first = await ReadIdAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: 5m), key));
        var again = await ReadIdAsync(await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: 5m), key));
        var other = await AdjustmentRequestHttp.PostAsync(world.TenantAdmin, world.Body(amount: 6m), key);

        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        again.ShouldBe(first);
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AdjustmentRequestHttp.CountAsync(world.AccountId)).ShouldBe(1);
    }

    [Test]
    public async Task List_IsNewestFirst_AndFiltersByStatusAccountAndCustomer()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var spare = await OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Spare", "USD");
        var first = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var second = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, AdjustmentWorld.Body(spare, amount: 6m));

        (await AdjustmentRequestHttp.ListIdsAsync(world.TenantAdmin)).ShouldBe([second, first]);
        (await AdjustmentRequestHttp.ListIdsAsync(world.TenantAdmin, "?status=Requested")).ShouldBe([second, first]);
        (await AdjustmentRequestHttp.ListIdsAsync(world.TenantAdmin, "?status=Approved,Rejected,Cancelled")).ShouldBeEmpty();
        (await AdjustmentRequestHttp.ListIdsAsync(world.TenantAdmin, $"?accountId={world.AccountId}")).ShouldBe([first]);
        (await AdjustmentRequestHttp.ListIdsAsync(world.TenantAdmin, $"?customerId={world.Customer.PublicId}")).ShouldBe([second, first]);
        (await AdjustmentRequestHttp.ListIdsAsync(world.TenantAdmin, $"?accountId={Guid.NewGuid()}")).ShouldBeEmpty();
        (await AdjustmentRequestHttp.ListIdsAsync(world.TenantAdmin, $"?customerId={Guid.NewGuid()}")).ShouldBeEmpty();
    }

    [Test]
    public async Task List_UnknownStatus_Returns400AtStatus_AndATenantIdIs400()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();

        (await ErrorsAsync(await AdjustmentRequestHttp.Send(HttpMethod.Get, $"{AdjustmentRequestHttp.Path}?status=Requested,Posted", world.TenantAdmin)))
            .Keys.ShouldContain("Status");
        (await ErrorsAsync(await AdjustmentRequestHttp.Send(HttpMethod.Get, $"{AdjustmentRequestHttp.Path}?tenantId={world.Tenant.PublicId}", world.TenantAdmin)))
            .Keys.ShouldContain("TenantId");
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsRequest_Returns404_IsNotListed_AndTheirAccountOrInstrumentIs404()
    {
        var north = await AdjustmentRequestWorld.CreateAsync();
        var south = await AdjustmentRequestWorld.CreateAsync("South Advisory", "south-advisory");
        var southId = await AdjustmentRequestHttp.RequestAsync(south.TenantAdmin, south.Body(amount: 5m));

        (await AdjustmentRequestHttp.Send(HttpMethod.Get, $"{AdjustmentRequestHttp.Path}/{southId}", north.TenantAdmin))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdjustmentRequestHttp.ListIdsAsync(north.TenantAdmin)).ShouldBeEmpty();
        (await AdjustmentRequestHttp.ListIdsAsync(north.TenantAdmin, $"?accountId={south.AccountId}")).ShouldBeEmpty();
        (await AdjustmentRequestHttp.ListIdsAsync(north.TenantAdmin, $"?customerId={south.Customer.PublicId}")).ShouldBeEmpty();
        (await AdjustmentRequestHttp.PostAsync(north.TenantAdmin, AdjustmentWorld.Body(south.AccountId, amount: 5m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdjustmentRequestHttp.PostAsync(north.TenantAdmin,
                north.Body(security: AdjustmentWorld.Holding(south.InstrumentId, 1m, 25m))))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdjustmentRequestHttp.CountAsync(north.AccountId)).ShouldBe(0);
        (await AdjustmentRequestHttp.CountAsync(south.AccountId)).ShouldBe(1);
    }
}
