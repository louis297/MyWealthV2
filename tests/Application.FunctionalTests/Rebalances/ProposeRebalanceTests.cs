using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R1, R4–R10, R27 (docs/features/rebalances.md §10, proposal): a person asks for a rebalance of
/// an assigned account and sees what it would trade, before anything is traded.</summary>
[Parallelizable]
public class ProposeRebalanceTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_AndACustomerOrASystemAdmin_Returns403_OnTheRebalanceRoutes()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var path in new[] { "/rebalances", $"/rebalances/{id}" })
        {
            (await RebalanceHttp.Send(HttpMethod.Get, path, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await RebalanceHttp.Send(HttpMethod.Get, path, world.CustomerToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await RebalanceHttp.Send(HttpMethod.Get, path, system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    public async Task AnAdviserOfAnotherCustomer_Gets404_AndDoesNotSeeItInTheList()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        var bob = await world.Orders.OtherAdviserAsync();

        (await RebalanceHttp.Send(HttpMethod.Get, $"/rebalances/{id}", bob)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RebalanceHttp.ListIdsAsync(bob)).ShouldNotContain(id);
        (await RebalanceHttp.ProposeAsync(bob, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Propose_StoresAProposal_WithItsLinesAndOwnTargets_AndPlacesNoOrderAndReservesNothing()
    {
        var world = await RebalanceWorld.CreateAsync();

        var (status, id) = await world.ProposeAsync();

        status.ShouldBe("Proposed");
        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("customerId").GetGuid().ShouldBe(world.Orders.Customer.PublicId);
        item.GetProperty("portfolio").GetProperty("id").GetGuid().ShouldBe(world.PortfolioId);
        item.GetProperty("portfolio").GetProperty("name").GetString().ShouldBe("Demo Growth");
        item.GetProperty("trigger").GetString().ShouldBe("Account");
        item.GetProperty("status").GetString().ShouldBe("Proposed");
        item.GetProperty("baseCurrency").GetString().ShouldBe("USD");
        item.GetProperty("totalValue").GetDecimal().ShouldBe(10000m);
        item.GetProperty("cash").GetDecimal().ShouldBe(10000m);
        item.GetProperty("minimumTrade").GetDecimal().ShouldBe(100m);
        item.GetProperty("cashResidualPercent").GetDecimal().ShouldBe(1m);
        item.GetProperty("targets").EnumerateArray()
            .Select(target => (target.GetProperty("instrument").GetProperty("symbol").GetString(), target.GetProperty("targetWeight").GetDecimal()))
            .ShouldBe([("ACME", 60m), ("BETA", 40m)]);
        var lines = item.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(line => (
                line.GetProperty("instrument").GetProperty("symbol").GetString(),
                line.GetProperty("quantity").GetDecimal(),
                line.GetProperty("weight").GetDecimal(),
                line.GetProperty("targetWeight").GetDecimal(),
                line.GetProperty("side").GetString(),
                line.GetProperty("tradeQuantity").GetDecimal(),
                line.GetProperty("referencePrice").GetDecimal(),
                line.GetProperty("estimatedGross").GetDecimal(),
                line.GetProperty("estimatedFee").GetDecimal(),
                line.GetProperty("currency").GetString()))
            .ShouldBe([
                ("ACME", 0m, 0m, 60m, "Buy", 59.4m, 100m, 5940m, 29.70m, "USD"),
                ("BETA", 0m, 0m, 40m, "Buy", 79.2m, 50m, 3960m, 19.80m, "USD")
            ]);
        lines[0].GetProperty("referencePriceDate").GetString().ShouldBe(OrderHttp.Today.ToString("yyyy-MM-dd"));
        item.GetProperty("orders").GetArrayLength().ShouldBe(0);
        item.GetProperty("waves").GetInt32().ShouldBe(0);
        item.GetProperty("failure").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("proposedBy").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        var proposedAt = item.GetProperty("proposedAt").GetDateTimeOffset();
        item.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(proposedAt.AddHours(24));
        item.GetProperty("confirmedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("cancelRequest").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("cancelReason").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("followUp").GetBoolean().ShouldBeFalse();
        item.GetProperty("endedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();

        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
        (await world.Orders.CashAsync()).ShouldBe((10000m, 0m, 10000m));
    }

    [Test]
    public async Task Propose_OfAnAccountWithNothingToTrade_StoresNoChange()
    {
        var world = await RebalanceWorld.CreateAsync(funding: 50m);

        var (status, id) = await world.ProposeAsync();

        status.ShouldBe("NoChange");
        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("status").GetString().ShouldBe("NoChange");
        item.GetProperty("lines").GetArrayLength().ShouldBe(0);
        item.GetProperty("endedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Propose_WithAPriceFiveDaysOld_Returns400StalePrice_AndStoresAFailedRebalance()
    {
        var world = await RebalanceWorld.CreateAsync(betaPriceDate: OrderHttp.Today.AddDays(-5));

        await ShouldFailAsync(world, "stale_price", $"The latest price of BETA is from {OrderHttp.Today.AddDays(-5):yyyy-MM-dd}. An order needs a price from the last 4 days.");
    }

    [Test]
    public async Task Propose_WithADisabledInstrument_Returns400InstrumentInactive()
    {
        var world = await RebalanceWorld.CreateAsync();
        await RebalanceHttp.DisableInstrumentAsync(world.BetaId);

        await ShouldFailAsync(world, "instrument_inactive", "BETA is disabled. A rebalance trades only active instruments.");
    }

    [Test]
    public async Task Propose_WithATargetQuotedInAnotherCurrency_Returns400CurrencyMismatch()
    {
        var world = await RebalanceWorld.CreateAsync(assign: false);
        var kiwi = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "KIWI", "NZD", 10m);
        var portfolio = await RebalanceHttp.CreatePortfolioAsync(world.TenantAdmin, "USD", (world.AcmeId, 50m), (kiwi, 50m));
        await RebalanceHttp.InsertAssignmentAsync(world.AccountId, portfolio);

        await ShouldFailAsync(world, "currency_mismatch",
            "KIWI is quoted in NZD and this account is in USD. A rebalance buys only instruments quoted in the account's currency.");
    }

    [Test]
    public async Task Propose_WithAHoldingQuotedInAnotherCurrency_ProposesToSellIt()
    {
        var world = await RebalanceWorld.CreateAsync();
        var kiwi = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "KIWI", "NZD", 10m);
        await RebalanceHttp.SetRateAsync("NZD", "USD", 0.6m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, kiwi, 20m, price: 10m);

        var (status, id) = await world.ProposeAsync();

        status.ShouldBe("Proposed");
        var kiwiLine = (await RebalanceHttp.GetAsync(world.Adviser, id)).GetProperty("lines").EnumerateArray()
            .Single(line => line.GetProperty("instrument").GetProperty("symbol").GetString() == "KIWI");
        kiwiLine.GetProperty("side").GetString().ShouldBe("Sell");
        kiwiLine.GetProperty("tradeQuantity").GetDecimal().ShouldBe(20m);
        kiwiLine.GetProperty("targetWeight").ValueKind.ShouldBe(JsonValueKind.Null);
        kiwiLine.GetProperty("currency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task Propose_WhenTheFirmHasNoBrokerConnection_Returns400NoBrokerConnection()
    {
        var world = await RebalanceWorld.CreateAsync();
        await OrderHttp.SetBrokerConnectionAsync(world.Tenant.Id, null);

        await ShouldFailAsync(world, "no_broker_connection",
            "The firm has no broker connection, so a rebalance cannot place orders.");
    }

    [Test]
    public async Task Propose_ForADisabledCustomer_Returns400AccountNotEligible()
    {
        var world = await RebalanceWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Orders.Customer.Id, disabled: true);

        await ShouldFailAsync(world, "account_not_eligible", "The account's customer is disabled. Enable the customer first.");
    }

    [Test]
    public async Task Propose_OfAnAccountWithNoAssignment_Returns400AccountNotEligible_AndStoresNothing()
    {
        var world = await RebalanceWorld.CreateAsync(assign: false);

        var response = await RebalanceHttp.ProposeAsync(world.Adviser, world.AccountId);

        (await RebalanceHttp.CodeAsync(response)).ShouldBe("account_not_eligible");
        (await RebalanceHttp.RebalancesOfAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task ASecondRequest_SupersedesTheFirstProposal()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, first) = await world.ProposeAsync();

        var (status, second) = await world.ProposeAsync();

        status.ShouldBe("Proposed");
        second.ShouldNotBe(first);
        var older = await RebalanceHttp.GetAsync(world.Adviser, first);
        older.GetProperty("status").GetString().ShouldBe("Cancelled");
        older.GetProperty("cancelReason").GetString().ShouldBe("Superseded");
        older.GetProperty("endedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await RebalanceHttp.GetAsync(world.Adviser, second)).GetProperty("status").GetString().ShouldBe("Proposed");
    }

    [Test]
    public async Task List_IsNewestFirst_WithoutDetail_AndFilters()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, first) = await world.ProposeAsync();
        var (_, second) = await world.ProposeAsync();

        var items = await RebalanceHttp.ListAsync(world.TenantAdmin, $"?accountId={world.AccountId}");
        items.Select(item => item.GetProperty("id").GetGuid()).ShouldBe([second, first]);
        items[0].TryGetProperty("lines", out _).ShouldBeFalse();
        items[0].TryGetProperty("targets", out _).ShouldBeFalse();
        items[0].TryGetProperty("orders", out _).ShouldBeFalse();
        items[0].GetProperty("portfolio").GetProperty("name").GetString().ShouldBe("Demo Growth");

        (await RebalanceHttp.ListIdsAsync(world.TenantAdmin, "?status=Proposed")).ShouldBe([second]);
        (await RebalanceHttp.ListIdsAsync(world.TenantAdmin, "?status=Cancelled,Proposed")).ShouldBe([second, first]);
        (await RebalanceHttp.ListIdsAsync(world.TenantAdmin, "?open=true")).ShouldBe([second]);
        (await RebalanceHttp.ListIdsAsync(world.TenantAdmin, $"?portfolioId={world.PortfolioId}")).ShouldBe([second, first]);
        (await RebalanceHttp.ListIdsAsync(world.TenantAdmin, $"?portfolioId={Guid.NewGuid()}")).ShouldBeEmpty();
        (await RebalanceHttp.ListIdsAsync(world.TenantAdmin, $"?customerId={world.Orders.Customer.PublicId}")).ShouldBe([second, first]);
        (await RebalanceHttp.ListIdsAsync(world.TenantAdmin, $"?accountId={Guid.NewGuid()}")).ShouldBeEmpty();
    }

    [Test]
    public async Task List_WithAnUnknownStatus_OrATenantId_Returns400()
    {
        var world = await RebalanceWorld.CreateAsync();

        var status = await RebalanceHttp.Send(HttpMethod.Get, "/rebalances?status=Sold", world.TenantAdmin);
        status.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await status.Content.ReadAsStringAsync()).ShouldContain("Status");
        (await RebalanceHttp.Send(HttpMethod.Get, $"/rebalances?tenantId={Guid.NewGuid()}", world.TenantAdmin)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnotherTenantsRebalance_Returns404_AndIsNotListed()
    {
        var south = await RebalanceWorld.CreateAsync("South Advisory", "south-advisory");
        var (_, theirs) = await south.ProposeAsync();
        var north = await RebalanceWorld.CreateAsync();

        (await RebalanceHttp.Send(HttpMethod.Get, $"/rebalances/{theirs}", north.TenantAdmin)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await RebalanceHttp.ListIdsAsync(north.TenantAdmin)).ShouldNotContain(theirs);
        (await RebalanceHttp.ProposeAsync(north.TenantAdmin, south.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task ShouldFailAsync(RebalanceWorld world, string code, string message)
    {
        var response = await RebalanceHttp.ProposeAsync(world.Adviser, world.AccountId);

        (await RebalanceHttp.CodeAsync(response)).ShouldBe(code);
        (await RebalanceHttp.DetailAsync(response)).ShouldBe(message);
        var stored = (await RebalanceHttp.ListAsync(world.TenantAdmin, $"?accountId={world.AccountId}")).ShouldHaveSingleItem();
        stored.GetProperty("status").GetString().ShouldBe("Failed");
        stored.GetProperty("failure").GetProperty("code").GetString().ShouldBe(code);
        stored.GetProperty("failure").GetProperty("message").GetString().ShouldBe(message);
        stored.GetProperty("totalValue").ValueKind.ShouldBe(JsonValueKind.Null);
        (await RebalanceHttp.RebalanceAsync(stored.GetProperty("id").GetGuid())).Status.ShouldBe(RebalanceStatus.Failed);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
    }
}
