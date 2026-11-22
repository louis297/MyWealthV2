using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Orders;

/// <summary>Orders R1, R6–R14: place an order, with every account, instrument and price check and its reservation.</summary>
[Parallelizable]
public class PlaceOrderTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_OnTheReadAndPlaceRoutes()
    {
        foreach (var (method, route, body) in OrderHttp.Routes(Guid.NewGuid()))
        {
            var response = await OrderHttp.Send(method, route, accessToken: null, body);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task SystemAdmin_Returns403_AndACustomerCannotPlace()
    {
        var world = await OrderWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var (method, route, body) in OrderHttp.Routes(Guid.NewGuid()))
        {
            (await TransactionHttp.Send(method, route, system.AccessToken, body, Guid.NewGuid()))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
        }

        (await OrderHttp.PostAsync(world.CustomerToken, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 1m)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task ABuy_ByAnAdviser_Returns201_Placed_WithItsPriceLimitAndWhatItReserves()
    {
        var world = await OrderWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var body = OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 10m);
        body["memo"] = "  Monthly top-up ";
        body["type"] = "Market";
        body["timeInForce"] = "Day";

        var response = await OrderHttp.PostAsync(world.Adviser, body);

        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"/orders/{id}");
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        var instrument = item.GetProperty("instrument");
        instrument.GetProperty("id").GetGuid().ShouldBe(world.InstrumentId);
        instrument.GetProperty("symbol").GetString().ShouldBe("ACME");
        instrument.GetProperty("name").GetString().ShouldBe("ACME Holdings");
        item.GetProperty("side").GetString().ShouldBe("Buy");
        item.GetProperty("type").GetString().ShouldBe("Market");
        item.GetProperty("timeInForce").GetString().ShouldBe("Day");
        item.GetProperty("quantity").GetDecimal().ShouldBe(10m);
        item.GetProperty("filledQuantity").GetDecimal().ShouldBe(0m);
        item.GetProperty("openQuantity").GetDecimal().ShouldBe(10m);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("referencePrice").GetDecimal().ShouldBe(100m);
        item.GetProperty("referencePriceDate").GetString().ShouldBe(OrderHttp.Today.ToString("yyyy-MM-dd"));
        item.GetProperty("priceLimit").GetDecimal().ShouldBe(105m);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(1055.25m);
        item.GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
        item.GetProperty("status").GetString().ShouldBe("Placed");
        item.GetProperty("memo").GetString().ShouldBe("Monthly top-up");
        item.GetProperty("placedBy").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        item.GetProperty("placedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        var send = item.GetProperty("send");
        send.GetProperty("attempts").GetInt32().ShouldBe(0);
        foreach (var name in new[] { "lastAttemptAt", "lastFailure", "acceptedAt" })
        {
            send.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null, name);
        }

        foreach (var name in new[] { "averagePrice", "rejectReason", "cancelRequest", "closedAt", "closeReason" })
        {
            item.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null, name);
        }

        item.GetProperty("fills").GetArrayLength().ShouldBe(0);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        (await OrderHttp.OrderAsync(id)).Connection.ShouldBe("simulator");
    }

    [Test]
    public async Task ABuy_WithNoScheduleForTheCurrency_ReservesTheMaximumGross()
    {
        var world = await OrderWorld.CreateAsync(feeSchedule: false);

        var id = await world.PlaceAsync("Buy", 10m);

        (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("reservedCash").GetDecimal().ShouldBe(1050m);
        (await OrderHttp.OrderAsync(id)).TradeFeeScheduleId.ShouldBeNull();
    }

    [Test]
    public async Task ASell_ReservesItsQuantity_AndNoCash_WithALimitBelowTheReferencePrice()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);

        var id = await world.PlaceAsync("sell", 30m);

        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("side").GetString().ShouldBe("Sell");
        item.GetProperty("priceLimit").GetDecimal().ShouldBe(95m);
        item.GetProperty("reservedQuantity").GetDecimal().ShouldBe(30m);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(0m);
        (await OrderHttp.OrderAsync(id)).TradeFeeScheduleId.ShouldNotBeNull();
    }

    [Test]
    public async Task Place_ReplayedWithTheSameKey_ReturnsTheSameId_AndAnotherBodyIs409_AndNoKeyIs400()
    {
        var world = await OrderWorld.CreateAsync();
        var key = Guid.NewGuid();
        var body = OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 2m);

        var first = await CustodialHttp.ReadIdAsync(await OrderHttp.PostAsync(world.Adviser, body, key));
        var again = await CustodialHttp.ReadIdAsync(await OrderHttp.PostAsync(world.Adviser, body, key));
        var other = await OrderHttp.PostAsync(world.Adviser, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 3m), key);
        var noKey = await OrderHttp.Send(HttpMethod.Post, OrderHttp.Path, world.Adviser, body);

        again.ShouldBe(first);
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(1);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(0.123456789)]
    public async Task Place_WithAQuantityNotAboveZero_OrWithMoreThan8Decimals_Returns400AtQuantity(decimal quantity)
    {
        var world = await OrderWorld.CreateAsync();

        var response = await world.TryPlaceAsync("Buy", quantity);

        (await CustodialHttp.ErrorsAsync(response))["Quantity"]
            .ShouldBe(["Quantity must be greater than 0 with at most 8 decimal places."]);
    }

    [Test]
    public async Task Place_OfAFractionOfAShare_IsAccepted()
    {
        var world = await OrderWorld.CreateAsync();

        var id = await world.PlaceAsync("Buy", 0.12345678m);

        (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("quantity").GetDecimal().ShouldBe(0.12345678m);
    }

    [TestCase("side", "Short", "Side")]
    [TestCase("type", "Limit", "Type")]
    [TestCase("timeInForce", "GoodTillCancelled", "TimeInForce")]
    [TestCase("price", "101.50", "Price")]
    [TestCase("priceLimit", "101.50", "PriceLimit")]
    [TestCase("limit", "101.50", "Limit")]
    [TestCase("amount", "1000", "Amount")]
    [TestCase("bookedAt", "2026-11-23T02:10:00Z", "BookedAt")]
    [TestCase("status", "Working", "Status")]
    [TestCase("tenantId", "0f8fad5b-d9cb-469f-a165-70867728950e", "TenantId")]
    [TestCase("id", "0f8fad5b-d9cb-469f-a165-70867728950e", "Id")]
    public async Task Place_WithAFieldTheRouteDoesNotTake_OrAValueItDoesNotAllow_Returns400AtThatField(string field, string value, string key)
    {
        var world = await OrderWorld.CreateAsync();
        var body = OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 1m);
        body[field] = value;

        var response = await OrderHttp.PostAsync(world.Adviser, body);

        (await CustodialHttp.ErrorsAsync(response)).Keys.ShouldContain(key);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task Place_WithAMemoOver200_Returns400AtMemo()
    {
        var world = await OrderWorld.CreateAsync();
        var body = OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 1m);
        body["memo"] = new string('m', 201);

        (await CustodialHttp.ErrorsAsync(await OrderHttp.PostAsync(world.Adviser, body)))["Memo"].ShouldBe(["Memo must be 1 to 200 characters."]);
    }

    [Test]
    public async Task Place_OnAnExternalAccount_Returns400AtAccountId()
    {
        var world = await OrderWorld.CreateAsync();
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);

        var response = await world.TryPlaceAsync("Buy", 1m, accountId: external);

        (await CustodialHttp.ErrorsAsync(response))["AccountId"].ShouldBe(["An order can be placed only on a Custodial account."]);
    }

    [TestCase(null)]
    [TestCase("not-configured")]
    public async Task Place_WhenTheFirmNamesNoConfiguredBrokerConnection_Returns400AtAccountId(string? connection)
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.SetBrokerConnectionAsync(world.Tenant.Id, connection);

        var response = await world.TryPlaceAsync("Buy", 1m);

        (await CustodialHttp.ErrorsAsync(response))["AccountId"]
            .ShouldBe(["The firm has no broker connection, so orders cannot be placed."]);
    }

    [Test]
    public async Task Place_OnAClosedAccount_Returns400()
    {
        var world = await OrderWorld.CreateAsync(funding: 0m);
        (await CustodialHttp.CloseAsync(world.TenantAdmin, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await world.TryPlaceAsync("Buy", 1m);

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
    }

    [Test]
    public async Task Place_OnAClosingAccount_Returns400()
    {
        var world = await OrderWorld.CreateAsync();
        var payee = await CustodialHttp.VerifiedPayeeAsync(world.AccountId);
        var closure = await TransactionHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/closures", world.Adviser,
            new Dictionary<string, object?> { ["nominatedBankAccountId"] = payee, ["memo"] = "Customer is leaving" }, Guid.NewGuid());
        closure.StatusCode.ShouldBe(HttpStatusCode.Created, await closure.Content.ReadAsStringAsync());

        var response = await world.TryPlaceAsync("Buy", 1m);

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("This account is being closed and accepts no new activity.");
    }

    [Test]
    public async Task Place_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await OrderWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await OrderHttp.PostAsync(world.TenantAdmin, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    [Test]
    public async Task Place_OnAnAccountThatFollowsAPortfolio_Returns400_WithTheTradeLockMessage()
    {
        var world = await OrderWorld.CreateAsync();
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", world.TenantAdmin, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = world.InstrumentId, targetWeight = 100m } }
        });
        var portfolioId = await CustodialHttp.ReadIdAsync(created);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == world.AccountId);
            var portfolio = await db.Portfolios.IgnoreQueryFilters().SingleAsync(row => row.PublicId == portfolioId);
            var assignment = AccountPortfolioAssignment.Assign(null, account.TenantId, account.Id, portfolio.Id, "test", DateTimeOffset.UtcNow);
            assignment.Created = DateTimeOffset.UtcNow;
            assignment.CreatedBy = "test";
            db.AccountPortfolioAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        var response = await world.TryPlaceAsync("Buy", 1m);

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("This account is assigned to a portfolio. Manual security trades are not allowed.");
    }

    [Test]
    public async Task Place_InAMissingOrADisabledInstrument_Returns400AtInstrumentId()
    {
        var world = await OrderWorld.CreateAsync();
        var disabled = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "OFF");
        var read = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{disabled}", world.TenantAdmin);
        using (var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()))
        {
            var disable = await InstrumentHttp.Send(HttpMethod.Post, $"/instruments/{disabled}/disable", world.TenantAdmin,
                new { rowVersion = json.RootElement.GetProperty("rowVersion").GetString() });
            disable.StatusCode.ShouldBe(HttpStatusCode.NoContent, await disable.Content.ReadAsStringAsync());
        }

        (await CustodialHttp.ErrorsAsync(await world.TryPlaceAsync("Buy", 1m, instrumentId: Guid.NewGuid())))["InstrumentId"]
            .ShouldBe(["Instrument was not found."]);
        (await CustodialHttp.ErrorsAsync(await world.TryPlaceAsync("Buy", 1m, instrumentId: disabled)))["InstrumentId"]
            .ShouldBe(["Instrument is disabled."]);
    }

    [Test]
    public async Task ABuy_InAnInstrumentQuotedInAnotherCurrency_Returns400_AndASellOfItIsAccepted()
    {
        var world = await OrderWorld.CreateAsync();
        var kiwi = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "KIWI", "NZD", 10m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, kiwi, 20m, price: 10m);

        var buy = await world.TryPlaceAsync("Buy", 1m, instrumentId: kiwi);
        var sell = await world.TryPlaceAsync("Sell", 5m, instrumentId: kiwi);

        (await CustodialHttp.ErrorsAsync(buy))["InstrumentId"].ShouldBe([
            "KIWI is quoted in NZD and this account is in USD. Convert cash to a NZD Custodial account and buy there."
        ]);
        var id = await CustodialHttp.ReadIdAsync(sell);
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("currency").GetString().ShouldBe("NZD");
        item.GetProperty("priceLimit").GetDecimal().ShouldBe(9.5m);
        item.GetProperty("reservedQuantity").GetDecimal().ShouldBe(5m);
    }

    [Test]
    public async Task Place_WithNoStoredPrice_Returns400AtInstrumentId()
    {
        var world = await OrderWorld.CreateAsync();
        var unpriced = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "NOPX", price: null);

        var response = await world.TryPlaceAsync("Buy", 1m, instrumentId: unpriced);

        (await CustodialHttp.ErrorsAsync(response))["InstrumentId"].ShouldBe(["No price is stored for NOPX."]);
    }

    [Test]
    public async Task Place_WithAPriceFiveDaysOld_Returns400_AndFourDaysOldIsAccepted()
    {
        var world = await OrderWorld.CreateAsync();
        var five = OrderHttp.Today.AddDays(-5);
        var stale = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "OLD5", price: 50m, priceDate: five);
        var fresh = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "OLD4", price: 50m, priceDate: OrderHttp.Today.AddDays(-4));

        var refused = await world.TryPlaceAsync("Buy", 1m, instrumentId: stale);
        var accepted = await world.TryPlaceAsync("Buy", 1m, instrumentId: fresh);

        (await CustodialHttp.ErrorsAsync(refused))["InstrumentId"].ShouldBe([
            $"The latest price of OLD5 is from {five:yyyy-MM-dd}. An order needs a price from the last 4 days."
        ]);
        var id = await CustodialHttp.ReadIdAsync(accepted);
        (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("referencePriceDate").GetString()
            .ShouldBe(OrderHttp.Today.AddDays(-4).ToString("yyyy-MM-dd"));
    }

    [Test]
    public async Task ABuy_ThatCanCostMoreThanTheAvailableCash_Returns400AtQuantity_AndOneThatFitsExactlyIsAccepted()
    {
        var world = await OrderWorld.CreateAsync(funding: 1055.25m);
        await CustodialHttp.RequestWithdrawalAsync(world.AccountId, 155.25m);

        var response = await world.TryPlaceAsync("Buy", 10m);

        (await CustodialHttp.ErrorsAsync(response))["Quantity"].ShouldBe([
            "This order can cost up to 1055.25 USD with fees. The account's available cash is 900.00 USD."
        ]);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);

        var exact = await OrderWorld.CreateAsync("South Advisory", "south-advisory", funding: 1055.25m);
        (await exact.TryPlaceAsync("Buy", 10m)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task ASell_OfMoreThanTheAvailableQuantity_Returns400AtQuantity_CountingTheSellOrdersAlreadyPlaced()
    {
        var world = await OrderWorld.CreateAsync();

        (await CustodialHttp.ErrorsAsync(await world.TryPlaceAsync("Sell", 1m)))["Quantity"]
            .ShouldBe(["The account has 0 ACME available to sell."]);

        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        (await CustodialHttp.ErrorsAsync(await world.TryPlaceAsync("Sell", 40.5m)))["Quantity"]
            .ShouldBe(["The account has 40 ACME available to sell."]);

        await world.PlaceAsync("Sell", 30m);
        (await CustodialHttp.ErrorsAsync(await world.TryPlaceAsync("Sell", 10.5m)))["Quantity"]
            .ShouldBe(["The account has 10 ACME available to sell."]);
        (await world.TryPlaceAsync("Sell", 10m)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AnAdviserOfAnotherCustomer_Gets404_AndATenantAdminMayPlace()
    {
        var world = await OrderWorld.CreateAsync();
        var bob = await world.OtherAdviserAsync();

        var response = await OrderHttp.PostAsync(bob, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 1m));
        var byAdmin = await OrderHttp.PostAsync(world.TenantAdmin, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var id = await CustodialHttp.ReadIdAsync(byAdmin);
        (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("placedBy").GetProperty("name").GetString().ShouldBe("Tess Admin");
        (await OrderHttp.Send(HttpMethod.Get, $"/orders/{id}", bob)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AnotherTenantsAccount_Is404_AndItsInstrumentIs400_AndItsOrderIs404()
    {
        var north = await OrderWorld.CreateAsync();
        var south = await OrderWorld.CreateAsync("South Advisory", "south-advisory");
        var southOrder = await south.PlaceAsync("Buy", 1m);

        (await north.TryPlaceAsync("Buy", 1m, accountId: south.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustodialHttp.ErrorsAsync(await north.TryPlaceAsync("Buy", 1m, instrumentId: south.InstrumentId)))["InstrumentId"]
            .ShouldBe(["Instrument was not found."]);
        foreach (var token in new[] { north.TenantAdmin, north.Adviser, north.CustomerToken })
        {
            (await OrderHttp.Send(HttpMethod.Get, $"/orders/{southOrder}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await OrderHttp.ListIdsAsync(north.TenantAdmin)).ShouldBeEmpty();
        (await OrderHttp.ListIdsAsync(south.TenantAdmin)).ShouldBe([southOrder]);
        (await OrderHttp.OrderCountAsync(south.AccountId)).ShouldBe(1);
    }
}
