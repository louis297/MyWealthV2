using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Rebalances;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R1, R20–R25, R28, R29: the permissions, the request and the reads of a transfer.</summary>
[Parallelizable]
public class TransferRequestTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect_OnEveryRoute()
    {
        var id = Guid.NewGuid();
        foreach (var (method, route) in new[]
                 {
                     (HttpMethod.Get, TransferHttp.Path), (HttpMethod.Get, $"{TransferHttp.Path}/{id}"), (HttpMethod.Post, TransferHttp.Path),
                     (HttpMethod.Post, $"{TransferHttp.Path}/{id}/approve"), (HttpMethod.Post, $"{TransferHttp.Path}/{id}/reject"),
                     (HttpMethod.Post, $"{TransferHttp.Path}/{id}/cancel"), (HttpMethod.Post, $"{TransferHttp.Path}/{id}/set-aside")
                 })
        {
            var response = await TransferHttp.Send(method, route, accessToken: null, new { rowVersion = TransferHttp.StaleRowVersion });

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task CustomerAndSystemAdmin_Return403_OnTheRequest_AndSystemAdminOnTheReads()
    {
        var world = await TransferWorld.CreateAsync();
        var system = await TenantHttp.SignInSystemAdmin();

        (await TransferHttp.PostAsync(world.CustomerToken, world.Body())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TransferHttp.PostAsync(system.AccessToken, world.Body())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TransferHttp.Send(HttpMethod.Get, TransferHttp.Path, system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin)).ShouldBeEmpty();
    }

    [Test]
    public async Task RequestIn_Returns201Requested_WithLocation_AndBooksNothing()
    {
        var world = await TransferWorld.CreateAsync();
        var transactionsBefore = await TransferHttp.TransactionCountAsync(world.AccountId);

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body());

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"/in-kind-transfers/{id}");
        var item = await TransferHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        item.GetProperty("direction").GetString().ShouldBe("In");
        item.GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(world.InstrumentId);
        item.GetProperty("instrument").GetProperty("symbol").GetString().ShouldBe("ACME");
        item.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("cost").GetDecimal().ShouldBe(9500.00m);
        item.GetProperty("acquiredOn").GetString().ShouldBe("2024-03-18");
        item.GetProperty("changesBeneficialOwner").GetBoolean().ShouldBeFalse();
        item.GetProperty("counterparty").GetProperty("name").GetString().ShouldBe("Harbour Brokers");
        item.GetProperty("counterparty").GetProperty("account").GetString().ShouldBe("HB-0044172");
        item.GetProperty("counterparty").GetProperty("holderName").GetString().ShouldBe("J Smith");
        item.GetProperty("memo").GetString().ShouldBe("Moving the Harbour holding across");
        item.GetProperty("status").GetString().ShouldBe("Requested");
        item.GetProperty("requestedBy").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        item.GetProperty("decision").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("send").GetProperty("attempts").GetInt32().ShouldBe(0);
        item.GetProperty("failureReason").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("setAside").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        (await TransferHttp.TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
    }

    [Test]
    public async Task RequestOut_HasNoCostOrAcquiredOn()
    {
        var world = await TransferWorld.CreateAsync();

        var id = await world.RequestAsync("Out", 40m);

        var item = await TransferHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("direction").GetString().ShouldBe("Out");
        item.GetProperty("cost").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("acquiredOn").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Request_ReplayedWithTheSameKey_ReturnsTheSameId_AndAnotherBodyIs409_AndNoKeyIs400()
    {
        var world = await TransferWorld.CreateAsync();
        var key = Guid.NewGuid();

        var first = await CustodialHttp.ReadIdAsync(await TransferHttp.PostAsync(world.Adviser, world.Body(), key));
        var again = await CustodialHttp.ReadIdAsync(await TransferHttp.PostAsync(world.Adviser, world.Body(), key));
        var other = await TransferHttp.PostAsync(world.Adviser, world.Body(quantity: 101m), key);
        var noKey = await TransactionHttp.Send(HttpMethod.Post, TransferHttp.Path, world.Adviser, world.Body());

        again.ShouldBe(first);
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin)).ShouldBe([first]);
    }

    [Test]
    public async Task Request_OnAnExternalAccount_Returns400AtAccountId()
    {
        var world = await TransferWorld.CreateAsync();
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);

        var response = await TransferHttp.PostAsync(world.Adviser, TransferHttp.Body(external, world.InstrumentId));

        (await CustodialHttp.ErrorsAsync(response))["AccountId"]
            .ShouldBe(["Securities can be transferred in kind only on a Custodial account."]);
    }

    [Test]
    public async Task Request_OnAClosedAccount_Returns400()
    {
        var world = await TransferWorld.CreateAsync(holding: 0m);
        await OrderHttp.SetAccountStatusAsync(world.AccountId, Domain.Enums.AccountStatus.Closed);

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body());

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
    }

    [Test]
    public async Task Request_OnAClosingAccount_Returns400()
    {
        var world = await TransferWorld.CreateAsync(holding: 0m);
        var payee = await CustodialHttp.VerifiedPayeeAsync(world.AccountId);
        var closure = await TransactionHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/closures", world.Adviser,
            new Dictionary<string, object?> { ["nominatedBankAccountId"] = payee, ["memo"] = "Customer is leaving" }, Guid.NewGuid());
        closure.StatusCode.ShouldBe(HttpStatusCode.Created, await closure.Content.ReadAsStringAsync());

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body());

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("This account is being closed and accepts no new activity.");
    }

    [Test]
    public async Task Request_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await TransferWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await TransferHttp.PostAsync(world.TenantAdmin, world.Body());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    [TestCase("In")]
    [TestCase("Out")]
    public async Task Request_OnAnAccountThatFollowsAPortfolio_Returns400AtAccountId(string direction)
    {
        var world = await TransferWorld.CreateAsync();
        var portfolio = await RebalanceHttp.CreatePortfolioAsync(world.TenantAdmin, "USD", (world.InstrumentId, 100m));
        await RebalanceHttp.InsertAssignmentAsync(world.AccountId, portfolio);

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body(direction, 10m));

        (await CustodialHttp.ErrorsAsync(response))["AccountId"]
            .ShouldBe(["This account follows a portfolio. Unassign the portfolio before transferring securities."]);
    }

    [TestCase("In")]
    [TestCase("Out")]
    public async Task Request_WithNoActiveSafekeepingAccountInTheQuoteCurrency_Returns400AtInstrumentId(string direction)
    {
        var world = await TransferWorld.CreateAsync(safekeeping: false);
        await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "NZD");

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body(direction, 10m));

        (await CustodialHttp.ErrorsAsync(response))["InstrumentId"]
            .ShouldBe(["The firm has no safekeeping account for securities quoted in USD, so ACME cannot be transferred."]);
    }

    [Test]
    public async Task Request_WhenTheSafekeepingAccountsConnectionIsNotConfigured_Returns400AtInstrumentId()
    {
        var world = await TransferWorld.CreateAsync();
        FunctionalTestSetup.WebApiServices.GetRequiredService<FakeCustodianConnections>().RemoveAll();

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body());

        (await CustodialHttp.ErrorsAsync(response))["InstrumentId"]
            .ShouldBe(["The firm has no safekeeping account for securities quoted in USD, so ACME cannot be transferred."]);
    }

    [Test]
    public async Task RequestIn_OfADisabledInstrument_Returns400_ButOutIsAllowed()
    {
        var world = await TransferWorld.CreateAsync();
        await TransferHttp.DisableInstrumentAsync(world.TenantAdmin, world.InstrumentId);

        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, world.Body())))["InstrumentId"]
            .ShouldBe(["Instrument is disabled."]);
        await world.RequestAsync("Out", 10m);
    }

    [Test]
    public async Task RequestIn_OfAnInstrumentInAnotherCurrency_Returns400WithTheMessage()
    {
        var world = await TransferWorld.CreateAsync();
        var nzd = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "KIWI", "NZD");
        await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "NZD");

        var response = await TransferHttp.PostAsync(world.Adviser, TransferHttp.Body(world.AccountId, nzd));

        (await CustodialHttp.ErrorsAsync(response))["InstrumentId"]
            .ShouldBe(["KIWI is quoted in NZD and this account is in USD. Transfer it into a NZD Custodial account."]);
    }

    [Test]
    public async Task Request_OfAMissingOrAnotherTenantsInstrument_Returns400AtInstrumentId()
    {
        var world = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");

        foreach (var instrument in new[] { Guid.NewGuid(), south.InstrumentId })
        {
            (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, TransferHttp.Body(world.AccountId, instrument))))
                ["InstrumentId"].ShouldBe(["Instrument was not found."]);
        }
    }

    [Test]
    public async Task Request_OfAnotherCustomersOrTenantsAccount_Returns404()
    {
        var world = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        var olgas = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.Orders.People.OtherCustomer.PublicId);

        foreach (var account in new[] { olgas, south.AccountId, Guid.NewGuid() })
        {
            (await TransferHttp.PostAsync(world.Adviser, TransferHttp.Body(account, world.InstrumentId)))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task RequestIn_WithoutACost_OrWithANegativeOne_OrAFutureAcquiredOn_Returns400AtTheField()
    {
        var world = await TransferWorld.CreateAsync();
        var noCost = world.Body();
        noCost.Remove("cost");
        var negative = world.Body();
        negative["cost"] = -1m;
        var places = world.Body();
        places["cost"] = 1.00001m;
        var future = world.Body();
        future["acquiredOn"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");

        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, noCost)))["Cost"]
            .ShouldBe(["Cost is required for a transfer in."]);
        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, negative))).ShouldContainKey("Cost");
        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, places))).ShouldContainKey("Cost");
        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, future))).ShouldContainKey("AcquiredOn");
    }

    [TestCase("cost", 5)]
    [TestCase("acquiredOn", "2024-01-01")]
    public async Task RequestOut_WithACostOrAcquiredOn_Returns400AtTheField(string field, object value)
    {
        var world = await TransferWorld.CreateAsync();
        var body = world.Body("Out", 10m);
        body[field] = value;

        var errors = await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, body));

        errors[char.ToUpperInvariant(field[0]) + field[1..]].ShouldBe(["Cost is given only for a transfer in."]);
    }

    [TestCase("quantity", 0)]
    [TestCase("quantity", 1.000000001)]
    [TestCase("direction", "Sideways")]
    [TestCase("changesBeneficialOwner", null)]
    [TestCase("memo", "")]
    public async Task Request_FieldOutsideItsRule_Returns400AtThatField(string field, object? value)
    {
        var world = await TransferWorld.CreateAsync();
        var body = world.Body();
        body[field] = value;
        if (field == "memo")
        {
            body[field] = new string('m', 201);
        }

        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, body)))
            .ShouldContainKey(char.ToUpperInvariant(field[0]) + field[1..]);
    }

    [TestCase("name", "")]
    [TestCase("account", "HB/1")]
    [TestCase("holderName", " ")]
    public async Task Request_CounterpartyFieldOutsideItsRule_Returns400AtThatField(string field, string value)
    {
        var world = await TransferWorld.CreateAsync();
        var body = world.Body();
        ((Dictionary<string, object?>)body["counterparty"]!)[field] = value;

        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, body)))
            .ShouldContainKey($"Counterparty.{char.ToUpperInvariant(field[0]) + field[1..]}");
    }

    [TestCase("status", "Completed")]
    [TestCase("bookedAt", "2026-11-24T00:00:00Z")]
    [TestCase("tenantId", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    [TestCase("id", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    public async Task Request_BodyCarriesAServerField_Returns400(string field, string value)
    {
        var world = await TransferWorld.CreateAsync();
        var body = world.Body();
        body[field] = value;

        (await TransferHttp.PostAsync(world.Adviser, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin)).ShouldBeEmpty();
    }

    [Test]
    public async Task List_FiltersByAccountCustomerInstrumentDirectionAndStatus_NewestFirst()
    {
        var world = await TransferWorld.CreateAsync();
        var first = await world.RequestAsync("In");
        var second = await world.RequestAsync("Out", 10m);

        (await TransferHttp.ListIdsAsync(world.TenantAdmin)).ShouldBe([second, first]);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin, $"?accountId={world.AccountId}")).ShouldBe([second, first]);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin, $"?customerId={world.Customer.PublicId}")).ShouldBe([second, first]);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin, $"?instrumentId={world.InstrumentId}")).ShouldBe([second, first]);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin, "?direction=Out")).ShouldBe([second]);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin, "?status=Requested,Approved")).ShouldBe([second, first]);
        (await TransferHttp.ListIdsAsync(world.TenantAdmin, "?status=Completed")).ShouldBeEmpty();
        (await CustodialHttp.ErrorsAsync(await TransferHttp.Send(HttpMethod.Get, $"{TransferHttp.Path}?status=Gone", world.TenantAdmin)))
            .ShouldContainKey("Status");
        (await TransferHttp.Send(HttpMethod.Get, $"{TransferHttp.Path}?tenantId={world.Tenant.PublicId}", world.TenantAdmin))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnAdviser_SeesTheirCustomersTransfersOnly()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();
        var bob = await world.Orders.OtherAdviserAsync();

        (await TransferHttp.ListIdsAsync(bob)).ShouldBeEmpty();
        (await TransferHttp.Send(HttpMethod.Get, $"{TransferHttp.Path}/{id}", bob)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransferHttp.ListIdsAsync(world.Adviser)).ShouldBe([id]);
    }

    [Test]
    public async Task ACustomer_ReadsOwnTransfers_WithoutTheMemoTheNotesAndTheSend()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();

        (await TransferHttp.ListIdsAsync(world.CustomerToken)).ShouldBe([id]);
        (await TransferHttp.ListIdsAsync(world.OtherCustomerToken)).ShouldBeEmpty();
        var item = await TransferHttp.GetAsync(world.CustomerToken, id);
        item.GetProperty("memo").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("send").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("counterparty").GetProperty("account").GetString().ShouldBe("HB-0044172");
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsTransfer_Returns404_AndIsNotListed()
    {
        var north = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        var id = await south.RequestAsync();

        (await TransferHttp.Send(HttpMethod.Get, $"{TransferHttp.Path}/{id}", north.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransferHttp.ListIdsAsync(north.TenantAdmin)).ShouldBeEmpty();
    }
}
