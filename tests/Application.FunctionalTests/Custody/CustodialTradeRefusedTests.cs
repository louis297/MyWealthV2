using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Transactions;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Orders R48: a hand <c>Buy</c> / <c>Sell</c> is refused on a Custodial account; a trade there goes through
/// an order.</summary>
[Parallelizable]
public class CustodialTradeRefusedTests : TestBase
{
    private const string Message = "Trades on a Custodial account go through the broker. Place an order.";

    [TestCase("Buy")]
    [TestCase("Sell")]
    public async Task AHandTrade_OnACustodialAccount_Returns400AtType_ForEveryCaller_AndWritesNothing(string type)
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin(
            "Trade Firm", "trade-firm", "tess@trade.example", adviserEmail: "ann@trade.example", customerEmail: "ned@trade.example");
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@trade.example", "Password1!", "trade-firm");
        var system = await TenantHttp.SignInSystemAdmin();
        var accountId = await OpenCustodialAsync(admin.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(admin.AccessToken, price: 25.00m);
        (await FundAsync(admin.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        // A holding to sell, from a trade typed before orders.
        await InsertManualTradeAsync(accountId, "Buy", instrumentId, 4m);
        var before = await TransactionCountAsync(accountId);

        foreach (var (token, tenantId) in new[] { (admin.AccessToken, (Guid?)null), (adviser.AccessToken, null), (system.AccessToken, tenant.PublicId) })
        {
            var key = Guid.NewGuid();
            var body = Trade(accountId, type, instrumentId, 1m);
            if (tenantId is not null)
            {
                body["tenantId"] = tenantId;
            }

            var response = await PostAsync(token, body, idempotencyKey: key);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await ErrorsAsync(response))["Type"].ShouldBe([Message]);
            (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
        }

        (await TransactionCountAsync(accountId)).ShouldBe(before);
        (await CashBalanceAsync(admin.AccessToken, accountId)).ShouldBe(900m);
    }

    [Test]
    public async Task AHandTrade_WithAPriceAFeeOrARate_OnACustodialAccount_IsRefusedTheSameWay()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: 25.00m);
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var priced = Trade(accountId, "Buy", instrumentId, 1m);
        priced["security"] = new[] { new { instrumentId, quantity = 1m, price = 25m } };
        var withFee = Trade(accountId, "Buy", instrumentId, 1m);
        withFee["fee"] = 1m;
        var withRate = Trade(accountId, "Buy", instrumentId, 1m);
        withRate["fxRate"] = 1m;
        var booked = Trade(accountId, "Buy", instrumentId, 1m).WithBookedAt();

        foreach (var body in new[] { priced, withFee, withRate, booked })
        {
            (await ErrorsAsync(await PostAsync(tokens.AccessToken, body)))["Type"].ShouldBe([Message]);
        }

        (await TransactionCountAsync(accountId)).ShouldBe(1);
    }

    [Test]
    public async Task AnExternalAccount_StillTakesAHandBuyAndSell()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: null);
        (await FundAsync(tokens.AccessToken, accountId, 1000m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        foreach (var (type, quantity) in new[] { ("Buy", 4m), ("Sell", 1m) })
        {
            var body = Trade(accountId, type, instrumentId, quantity).WithBookedAt();
            body["security"] = new[] { new { instrumentId, quantity, price = 25m } };
            (await PostAsync(tokens.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(925m);
    }

    [Test]
    public async Task ATradeTypedBeforeOrders_KeepsSourceManual_AndIsStillReversedByHand_WhileItIsTheLatestOnItsHolding()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: 25.00m);
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var buy = await InsertManualTradeAsync(accountId, "Buy", instrumentId, 4m);
        var sell = await InsertManualTradeAsync(accountId, "Sell", instrumentId, 1m);

        using (var item = await GetTransactionAsync(tokens.AccessToken, sell))
        {
            item.RootElement.GetProperty("source").GetProperty("kind").GetString().ShouldBe("Manual");
            item.RootElement.GetProperty("execution").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        }

        // The buy has a later trade on its holding; the sell is the latest.
        (await ReverseAsync(tokens.AccessToken, buy)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReverseAsync(tokens.AccessToken, sell)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReverseAsync(tokens.AccessToken, buy)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(1000m);
    }
}
