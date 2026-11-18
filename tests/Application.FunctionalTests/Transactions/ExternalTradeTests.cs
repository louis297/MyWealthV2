using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Fees;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>
/// External caller numbers R4–R14: a <c>Buy</c> / <c>Sell</c> on an External account takes the executed price, the fee
/// and the FX rate from the caller and reads no stored price, rate or trade fee schedule. Custodial accounts refuse them.
/// </summary>
[Parallelizable]
public class ExternalTradeTests : TestBase
{
    private const string BookedAt = "2026-11-17T22:30:00Z";
    private const string BookedDate = "2026-11-17";

    [Test]
    public async Task SameCurrencyBuy_WithPriceAndFee_BooksTheCallersNumbersWithoutAStoredPrice()
    {
        var world = await WorldAsync("NZD", "NZD");

        var response = await PostAsync(world, Trade(world, "Buy", 100m, price: 2.345m, fee: 15.00m));

        var id = await CustodialHttp.ReadIdAsync(response);
        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        var legs = TransactionJson.CashLegs(item.RootElement);
        legs.Select(leg => (leg.GetProperty("role").GetString(), leg.GetProperty("amount").GetDecimal(), leg.GetProperty("currency").GetString()))
            .ShouldBe([("Principal", -234.5000m, "NZD"), ("Fee", -15.0000m, "NZD")], ignoreOrder: true);
        var fee = legs.Single(leg => leg.GetProperty("role").GetString() == "Fee").GetProperty("fee");
        fee.GetProperty("kind").GetString().ShouldBe("Brokerage");
        fee.GetProperty("scheduleId").ValueKind.ShouldBe(JsonValueKind.Null);
        fee.GetProperty("basisAmount").GetDecimal().ShouldBe(234.5000m);
        fee.GetProperty("amount").GetDecimal().ShouldBe(15.00m);
        fee.GetProperty("currency").GetString().ShouldBe("NZD");
        var security = TransactionJson.SecurityLegs(item.RootElement).ShouldHaveSingleItem();
        security.GetProperty("cost").GetDecimal().ShouldBe(249.5000m);
        security.GetProperty("costCurrency").GetString().ShouldBe("NZD");
        item.RootElement.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Buy_WithoutAFee_BooksNoFeeLegEvenWhenTheTenantHasASchedule()
    {
        var world = await WorldAsync("NZD", "NZD");
        await FeeHttp.AddScheduleAsync(world.TenantId, "NZD", FeeHttp.ScheduleFrom);

        var id = await CustodialHttp.ReadIdAsync(await PostAsync(world, Trade(world, "Buy", 100m, price: 2.345m)));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        TransactionJson.CashLegs(item.RootElement).ShouldHaveSingleItem().GetProperty("amount").GetDecimal()
            .ShouldBe(-234.5000m);
        TransactionJson.SecurityLegs(item.RootElement).Single().GetProperty("cost").GetDecimal().ShouldBe(234.5000m);
    }

    [Test]
    public async Task Sell_WithPrice_ReleasesTheAverageCostAndBooksNoFeeLeg()
    {
        var world = await WorldAsync("NZD", "NZD");
        await FeeHttp.AddScheduleAsync(world.TenantId, "NZD", FeeHttp.ScheduleFrom);
        await CustodialHttp.ReadIdAsync(await PostAsync(world, Trade(world, "Buy", 100m, price: 2.345m, fee: 15.00m)));

        var id = await CustodialHttp.ReadIdAsync(await PostAsync(world, Trade(world, "Sell", 40m, price: 2.50m)));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        TransactionJson.CashLegs(item.RootElement).ShouldHaveSingleItem().GetProperty("amount").GetDecimal()
            .ShouldBe(100.0000m);
        TransactionJson.SecurityLegs(item.RootElement).Single().GetProperty("cost").GetDecimal().ShouldBe(-99.8000m);
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(100000m - 249.5m + 100m);
    }

    [Test]
    public async Task Buy_WithoutPrice_Returns400AndWritesNothing()
    {
        var world = await WorldAsync("NZD", "NZD");
        var key = Guid.NewGuid();

        var errors = await CustodialHttp.ErrorsAsync(await PostAsync(world, Trade(world, "Buy", 1m), key));

        errors["Security[0].Price"].ShouldBe(["Price is required on an External account. Enter the executed price."]);
        await AssertNothingWrittenAsync(world, key);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(1.123456789)]
    public async Task Price_NotPositiveOrWithMoreThanEightDecimals_Returns400(decimal price)
    {
        var world = await WorldAsync("NZD", "NZD");

        var errors = await CustodialHttp.ErrorsAsync(await PostAsync(world, Trade(world, "Sell", 1m, price: price)));

        errors.ShouldContainKey("Security[0].Price");
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task Fee_WithMoreDecimalsThanTheQuoteCurrency_Returns400()
    {
        var world = await WorldAsync("NZD", "NZD");

        var errors = await CustodialHttp.ErrorsAsync(
            await PostAsync(world, Trade(world, "Buy", 1m, price: 2m, fee: 1.005m)));

        errors["Fee"].ShouldBe(["Fee can have at most 2 decimal places."]);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task Fee_BelowZero_Returns400()
    {
        var world = await WorldAsync("NZD", "NZD");

        var errors = await CustodialHttp.ErrorsAsync(
            await PostAsync(world, Trade(world, "Buy", 1m, price: 2m, fee: -1m)));

        errors["Fee"].ShouldBe(["Fee cannot be negative."]);
    }

    [Test]
    public async Task CrossCurrencyBuy_WithoutFxRate_Returns400()
    {
        var world = await WorldAsync("NZD", "USD");

        var errors = await CustodialHttp.ErrorsAsync(
            await PostAsync(world, Trade(world, "Buy", 10m, price: 180.00m, fee: 9.95m)));

        errors["FxRate"].ShouldBe(["FxRate is required when the instrument currency differs from the account currency."]);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task CrossCurrencyBuy_WithFxRate_ConvertsWithTheCallersRateWithoutAStoredRate()
    {
        var world = await WorldAsync("NZD", "USD");

        var id = await CustodialHttp.ReadIdAsync(await PostAsync(
            world, Trade(world, "Buy", 10m, price: 180.00m, fee: 9.95m, fxRate: 1.69147m)));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        var legs = TransactionJson.CashLegs(item.RootElement);
        legs.Select(leg => (leg.GetProperty("role").GetString(), leg.GetProperty("amount").GetDecimal(), leg.GetProperty("currency").GetString()))
            .ShouldBe([("Principal", -3044.6460m, "NZD"), ("Fee", -16.8301m, "NZD")], ignoreOrder: true);
        var fee = legs.Single(leg => leg.GetProperty("role").GetString() == "Fee").GetProperty("fee");
        fee.GetProperty("scheduleId").ValueKind.ShouldBe(JsonValueKind.Null);
        fee.GetProperty("basisAmount").GetDecimal().ShouldBe(1800.00m);
        fee.GetProperty("amount").GetDecimal().ShouldBe(9.95m);
        fee.GetProperty("currency").GetString().ShouldBe("USD");
        var security = TransactionJson.SecurityLegs(item.RootElement).Single();
        security.GetProperty("cost").GetDecimal().ShouldBe(1809.9500m);
        security.GetProperty("costCurrency").GetString().ShouldBe("USD");
        AssertCallerFx(item.RootElement);
        (await FxConversionTests.FxDetailAsync(id)).ShouldBe(("USD", "NZD", 1.69147m, new DateOnly(2026, 11, 17), "Caller"));
    }

    [Test]
    public async Task CrossCurrencySell_WithFxRate_StoresTheFxDetail()
    {
        var world = await WorldAsync("NZD", "USD");
        await CustodialHttp.ReadIdAsync(await PostAsync(
            world, Trade(world, "Buy", 10m, price: 180.00m, fxRate: 1.69147m)));

        var id = await CustodialHttp.ReadIdAsync(await PostAsync(
            world, Trade(world, "Sell", 4m, price: 180.00m, fxRate: 1.69147m)));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        TransactionJson.CashLegs(item.RootElement).ShouldHaveSingleItem().GetProperty("amount").GetDecimal()
            .ShouldBe(1217.8584m);
        AssertCallerFx(item.RootElement);
    }

    [TestCase(0)]
    [TestCase(1.123456789)]
    public async Task FxRate_NotPositiveOrWithMoreThanEightDecimals_Returns400(decimal fxRate)
    {
        var world = await WorldAsync("NZD", "USD");

        var errors = await CustodialHttp.ErrorsAsync(
            await PostAsync(world, Trade(world, "Buy", 1m, price: 180m, fxRate: fxRate)));

        errors.ShouldContainKey("FxRate");
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task SameCurrencyTrade_WithFxRate_Returns400()
    {
        var world = await WorldAsync("NZD", "NZD");

        var errors = await CustodialHttp.ErrorsAsync(
            await PostAsync(world, Trade(world, "Buy", 1m, price: 2m, fxRate: 1m)));

        errors["FxRate"].ShouldBe(["FxRate cannot be set when the instrument currency is the account currency."]);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task OversellAndCashBelowZero_Return400()
    {
        var world = await WorldAsync("NZD", "NZD", cash: 100m);

        (await CustodialHttp.ErrorsAsync(await PostAsync(world, Trade(world, "Sell", 1m, price: 2m))))
            .ShouldContainKey(string.Empty);
        (await CustodialHttp.ErrorsAsync(await PostAsync(world, Trade(world, "Buy", 50m, price: 2m, fee: 0.01m))))
            .ShouldContainKey(string.Empty);
        await AssertNothingWrittenAsync(world);
        (await CustodialHttp.ReadIdAsync(await PostAsync(world, Trade(world, "Buy", 50m, price: 2m))))
            .ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task Reverse_CopiesTheOppositeLegsTheFeeDetailAndTheFxDetail()
    {
        var world = await WorldAsync("NZD", "USD");
        var buyId = await CustodialHttp.ReadIdAsync(await PostAsync(
            world, Trade(world, "Buy", 10m, price: 180.00m, fee: 9.95m, fxRate: 1.69147m)));

        var reversalId = await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(world.Token, buyId));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, reversalId);
        var legs = TransactionJson.CashLegs(item.RootElement);
        legs.Select(leg => (leg.GetProperty("role").GetString(), leg.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Principal", 3044.6460m), ("Fee", 16.8301m)], ignoreOrder: true);
        var fee = legs.Single(leg => leg.GetProperty("role").GetString() == "Fee").GetProperty("fee");
        fee.GetProperty("scheduleId").ValueKind.ShouldBe(JsonValueKind.Null);
        fee.GetProperty("amount").GetDecimal().ShouldBe(9.95m);
        TransactionJson.SecurityLegs(item.RootElement).Single().GetProperty("cost").GetDecimal().ShouldBe(-1809.9500m);
        AssertCallerFx(item.RootElement);
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(100000m);
    }

    [Test]
    public async Task Reverse_OfASameCurrencySell_RestoresTheHolding()
    {
        var world = await WorldAsync("NZD", "NZD");
        await CustodialHttp.ReadIdAsync(await PostAsync(world, Trade(world, "Buy", 100m, price: 2.345m, fee: 15.00m)));
        var sellId = await CustodialHttp.ReadIdAsync(await PostAsync(world, Trade(world, "Sell", 40m, price: 2.50m, fee: 5.00m)));

        var reversalId = await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(world.Token, sellId));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, reversalId);
        TransactionJson.CashLegs(item.RootElement).Select(leg => leg.GetProperty("amount").GetDecimal())
            .ShouldBe([-100.0000m, 5.0000m], ignoreOrder: true);
        TransactionJson.SecurityLegs(item.RootElement).Single().GetProperty("cost").GetDecimal().ShouldBe(99.8000m);
        item.RootElement.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task IdempotencyKey_ReplaysTheSameBodyAndRejectsAnotherPriceFeeOrRate()
    {
        var world = await WorldAsync("NZD", "USD");
        var key = Guid.NewGuid();
        var body = Trade(world, "Buy", 1m, price: 180m, fee: 1m, fxRate: 1.6m);
        var id = await CustodialHttp.ReadIdAsync(await PostAsync(world, body, key));

        (await CustodialHttp.ReadIdAsync(await PostAsync(world, Trade(world, "Buy", 1m, price: 180m, fee: 1m, fxRate: 1.6m), key)))
            .ShouldBe(id);
        (await PostAsync(world, Trade(world, "Buy", 1m, price: 181m, fee: 1m, fxRate: 1.6m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await PostAsync(world, Trade(world, "Buy", 1m, price: 180m, fee: 2m, fxRate: 1.6m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await PostAsync(world, Trade(world, "Buy", 1m, price: 180m, fee: 1m, fxRate: 1.7m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(2);
    }

    [TestCase("Buy")]
    [TestCase("Sell")]
    public async Task CustodialTrade_WithPriceFeeOrFxRate_Returns400OnItsFieldAndWritesNothing(string type)
    {
        var (_, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        (await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 10m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var before = await CustodialHttp.TransactionCountAsync(accountId);

        var price = CustodialHttp.Trade(accountId, type, instrumentId, 1m);
        price["security"] = new[] { new { instrumentId, quantity = 1m, price = 25m } };
        var fee = CustodialHttp.Trade(accountId, type, instrumentId, 1m);
        fee["fee"] = 1m;
        var fxRate = CustodialHttp.Trade(accountId, type, instrumentId, 1m);
        fxRate["fxRate"] = 1m;

        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(admin.AccessToken, price)))["Security[0].Price"]
            .ShouldBe(["Price cannot be set on a buy or sell."]);
        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(admin.AccessToken, fee)))["Fee"]
            .ShouldBe(["Fee cannot be set. Brokerage is added by the server from the trade fee schedule."]);
        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(admin.AccessToken, fxRate)))["FxRate"]
            .ShouldBe(["FxRate cannot be set on a Custodial account. The server reads the rate."]);
        (await CustodialHttp.TransactionCountAsync(accountId)).ShouldBe(before);
    }

    [Test]
    public async Task CustodialBuy_StillUsesTheStoredPriceAndTheSchedulesFee()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);

        var id = await CustodialHttp.ReadIdAsync(
            await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m));

        (await TradeFeeTradeTests.CashAmountsAsync(admin.AccessToken, id)).ShouldBe([-1250m, -10m], ignoreOrder: true);
    }

    [Test]
    public async Task FeeOrFxRateOnACashType_Returns400()
    {
        var world = await WorldAsync("NZD", "NZD");
        var fee = CustodialHttp.Cash(world.AccountId, "TransferIn", 10m).WithBookedAt(BookedAt);
        fee["fee"] = 1m;
        var fxRate = CustodialHttp.Cash(world.AccountId, "TransferIn", 10m).WithBookedAt(BookedAt);
        fxRate["fxRate"] = 1m;

        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(world.Token, fee))).ShouldContainKey("Fee");
        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(world.Token, fxRate))).ShouldContainKey("FxRate");
    }

    [Test]
    public async Task AnotherTenantsAccountOrInstrument_Returns404Not400()
    {
        var world = await WorldAsync("NZD", "NZD");
        var (_, _, southCustomer, south) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var southAccount = await AccountHttp.OpenAccountAsync(
            south.AccessToken, southCustomer.PublicId, "South brokerage", "Brokerage", "NZD");
        var southInstrument = await CustodialHttp.CreateInstrumentAsync(south.AccessToken, "AIA", "NZD", price: null);
        var southCustodial = await CustodialHttp.OpenCustodialAsync(south.AccessToken, southCustomer.PublicId, currency: "NZD");

        var onTheirAccount = Trade(world, "Buy", 1m, price: 2m);
        onTheirAccount["accountId"] = southAccount;
        (await PostAsync(world, onTheirAccount)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var withTheirInstrument = Trade(world, "Buy", 1m, price: 2m);
        withTheirInstrument["security"] = new[] { new { instrumentId = southInstrument, quantity = 1m, price = 2m } };
        (await PostAsync(world, withTheirInstrument)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Without a price, another tenant's External account is still 404, not the 400 for a missing price.
        var withoutPrice = Trade(world, "Buy", 1m);
        withoutPrice["accountId"] = southAccount;
        (await PostAsync(world, withoutPrice)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A price on another tenant's Custodial account is 404, not the 400 that refuses it.
        var priceOnTheirCustodial = CustodialHttp.Trade(southCustodial, "Buy", world.InstrumentId, 1m);
        priceOnTheirCustodial["security"] = new[] { new { instrumentId = world.InstrumentId, quantity = 1m, price = 2m } };
        (await CustodialHttp.PostAsync(world.Token, priceOnTheirCustodial)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await AssertNothingWrittenAsync(world);
    }

    private sealed record World(int TenantId, string Token, Guid AccountId, Guid InstrumentId, string AccountCurrency);

    /// <summary>An External brokerage account funded with 100,000 and an instrument with no stored price.</summary>
    private static async Task<World> WorldAsync(string accountCurrency, string quoteCurrency, decimal cash = 100000m)
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var accountId = await AccountHttp.OpenAccountAsync(
            admin.AccessToken, customer.PublicId, "Held elsewhere", "Brokerage", accountCurrency);
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(admin.AccessToken, "VTI", quoteCurrency, price: null);
        (await CustodialHttp.FundAsync(admin.AccessToken, accountId, cash, external: true)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        return new World(tenant.Id, admin.AccessToken, accountId, instrumentId, accountCurrency);
    }

    private static Dictionary<string, object?> Trade(
        World world,
        string type,
        decimal quantity,
        decimal? price = null,
        decimal? fee = null,
        decimal? fxRate = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = world.AccountId,
            ["type"] = type,
            ["bookedAt"] = BookedAt,
            ["security"] = price is null
                ? new object[] { new { instrumentId = world.InstrumentId, quantity } }
                : new object[] { new { instrumentId = world.InstrumentId, quantity, price } }
        };
        if (fee is not null)
        {
            body["fee"] = fee;
        }

        if (fxRate is not null)
        {
            body["fxRate"] = fxRate;
        }

        return body;
    }

    private static Task<HttpResponseMessage> PostAsync(World world, Dictionary<string, object?> body, Guid? key = null) =>
        CustodialHttp.PostAsync(world.Token, body, idempotencyKey: key);

    private static void AssertCallerFx(JsonElement item)
    {
        var fx = item.GetProperty("fx");
        fx.GetProperty("baseCurrency").GetString().ShouldBe("USD");
        fx.GetProperty("quoteCurrency").GetString().ShouldBe("NZD");
        fx.GetProperty("rate").GetDecimal().ShouldBe(1.69147m);
        fx.GetProperty("rateAsOfDate").GetString().ShouldBe(BookedDate);
    }

    /// <summary>Only the funding is on the account, and the refused key stored no idempotency record.</summary>
    private static async Task AssertNothingWrittenAsync(World world, Guid? key = null)
    {
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(1);
        if (key is not null)
        {
            (await CustodialHttp.IdempotencyRecordExistsAsync(key.Value)).ShouldBeFalse();
        }
    }
}
