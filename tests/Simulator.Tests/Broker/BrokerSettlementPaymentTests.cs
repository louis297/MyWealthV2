using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Bank;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Broker;

/// <summary>Trade-settlement R37–R39, R42: the broker pays its settlement receipts into the trust account at the
/// simulator bank, or holds them; its own books of settlements; the reset.</summary>
public class BrokerSettlementPaymentTests
{
    private const int Seed = 42;
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";
    private const string OtherOrderId = "7d1f3a55-0e0b-4c1e-8a57-5b7f0c2d9e42";
    private const string TrustUsd = "SIM-DEMO-USD";

    private static readonly Dictionary<string, string?> BrokerAccounts = new()
    {
        ["Simulator:Broker:BankAccounts:0:Currency"] = "USD",
        ["Simulator:Broker:BankAccounts:0:AccountNumber"] = "BRK-SIM-USD",
        ["Simulator:Broker:BankAccounts:0:Name"] = "Simulator Broker"
    };

    private SimulatorFactory _factory = null!;
    private HttpClient _control = null!;
    private HttpClient _party = null!;
    private string _prefix = "";

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new SimulatorFactory(settings: BrokerAccounts);
        _control = _factory.SessionClient();
        _party = _factory.PartyClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        _party.Dispose();
        _control.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public async Task ResetState()
    {
        _factory.Clock.Set(TestClock.Start);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BrokerHttp.ResetBroker);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BankHttp.ResetBank);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, $"UPDATE [World] SET [Seed] = {Seed};");
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);
        await BankHttp.OpenOkAsync(_control, TrustUsd);
        _prefix = (await MarketHttp.WorldIdAsync()).ToString("N")[..8];
    }

    [Test]
    public async Task InModePay_TheBrokerPaysANetAbove0_AsACreditLineOnTheTrustAccount_WithTheSettlementIdAsReference()
    {
        await SellAsync(TrustUsd);

        await SettleAsync();

        var line = (await BankHttp.FeedOkAsync(_party, TrustUsd)).Items.ShouldHaveSingleItem();
        (line.GetProperty("direction").GetString(), line.GetProperty("amount").GetDecimal(), line.GetProperty("reference").GetString())
            .ShouldBe(("Credit", 990m, $"{_prefix}-S1"));
        (line.GetProperty("payerName").GetString(), line.GetProperty("payerAccount").GetString())
            .ShouldBe(("Simulator Broker", "BRK-SIM-USD"));
        var settlement = (await SettlementsAsync()).Single();
        (settlement.GetProperty("paid").GetBoolean(), settlement.GetProperty("paidAmount").GetDecimal()).ShouldBe((true, 990m));
    }

    [Test]
    public async Task ASettlementAccountThatIsNotAnOpenAccount_GetsNoLine_AndIsNotPaid()
    {
        await SellAsync("SIM-UNKNOWN");

        await SettleAsync();

        (await BankHttp.FeedOkAsync(_party, TrustUsd)).Items.ShouldBeEmpty();
        var settlement = (await SettlementsAsync()).Single();
        (settlement.GetProperty("paid").GetBoolean(), settlement.GetProperty("paidAmount").GetDecimal()).ShouldBe((false, 0m));
    }

    [Test]
    public async Task InModeHold_NothingIsPaid_UntilTheOperatorPays_AWrongAmountAndThenTheNet()
    {
        (await SetSettlementModeAsync("Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await SellAsync(TrustUsd);
        await SettleAsync();
        (await BankHttp.FeedOkAsync(_party, TrustUsd)).Items.ShouldBeEmpty();

        var wrong = await PayAsync($"{_prefix}-S1", 500m);
        var right = await PayAsync($"{_prefix}-S1", null);

        wrong.StatusCode.ShouldBe(HttpStatusCode.OK, await wrong.Content.ReadAsStringAsync());
        right.StatusCode.ShouldBe(HttpStatusCode.OK, await right.Content.ReadAsStringAsync());
        (await BankHttp.FeedOkAsync(_party, TrustUsd)).Items.Select(item => item.GetProperty("amount").GetDecimal()).ShouldBe([500m, 990m]);
        (await SettlementsAsync()).Single().GetProperty("paidAmount").GetDecimal().ShouldBe(1490m);
        var actions = await BrokerHttp.LogActionsAsync();
        actions.ShouldContain("BrokerSettlementModeChanged");
        actions.Count(action => action == "BrokerSettlementPaid").ShouldBe(2);
        (await BrokerHttp.SettingsAsync(_control)).GetProperty("settlementMode").GetString().ShouldBe("Hold");
    }

    [TestCase(0, "Amount")]
    [TestCase(1.00001, "Amount")]
    public async Task Pay_WithAnAmountOutsideItsRules_Returns400AtAmount(decimal amount, string field)
    {
        await SetSettlementModeAsync("Hold");
        await SellAsync(TrustUsd);
        await SettleAsync();

        await RefusedAsync(await PayAsync($"{_prefix}-S1", amount), field);
    }

    [Test]
    public async Task Pay_ASettlementTheFirmOwes_Returns400AtStatus_AnUnknownOne404_AndAnUnknownTrustAccount400()
    {
        await SetSettlementModeAsync("Hold");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(settlementAccount: TrustUsd));
        await SellAsync("SIM-UNKNOWN", OtherOrderId);
        await SettleAsync();

        await RefusedAsync(await PayAsync($"{_prefix}-S1", null), "Status");
        (await PayAsync($"{_prefix}-S9", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await RefusedAsync(await PayAsync($"{_prefix}-S2", null), "SettlementAccount");
    }

    [Test]
    public async Task ASettlementMode_OtherThanPayOrHold_Returns400AtMode()
    {
        await RefusedAsync(await SetSettlementModeAsync("Later"), "Mode");
    }

    [Test]
    public async Task TheSettlements_AreListedNewestFirst_AndThePaidAmountOfANetBelow0_FollowsTheBanksPayments()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(quantity: 4m, referencePrice: 100m, settlementAccount: TrustUsd));
        await SettleAsync();
        await BankHttp.OpenOkAsync(_control, "SIM-DEMO-NZD", "NZD");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(
            OtherOrderId, currency: "NZD", quantity: 1m, referencePrice: 50m, settlementAccount: "SIM-DEMO-NZD"));
        await MarketHttp.AdvanceAsync(_control, "P4D");
        await BrokerHttp.SettingsAsync(_control);
        var payment = BankHttp.Payment(amount: 400m);
        payment["reference"] = $"{_prefix}-S1";
        var paid = await BankHttp.PayAsync(_party, TrustUsd, payment);
        paid.StatusCode.ShouldBe(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());

        var listed = await BrokerHttp.GetAsync(_control, "/control/broker/settlements");

        listed.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var items = listed.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetString()).ShouldBe([$"{_prefix}-S2", $"{_prefix}-S1"]);
        items[1].EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "settlementAccount", "currency", "settlementDate", "netAmount", "fillCount", "paid", "paidAmount"
        ]);
        (items[1].GetProperty("netAmount").GetDecimal(), items[1].GetProperty("fillCount").GetInt32(),
            items[1].GetProperty("paid").GetBoolean(), items[1].GetProperty("paidAmount").GetDecimal()).ShouldBe((-400m, 1, true, 400m));

        using var answer = JsonDocument.Parse(await paid.Content.ReadAsStringAsync());
        var paymentId = answer.RootElement.GetProperty("paymentId").GetString()!;
        (await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/return", """{ "reason": "AccountClosed" }"""))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await BrokerHttp.GetAsync(_control, "/control/broker/settlements")).GetProperty("items")[1];
        (after.GetProperty("paid").GetBoolean(), after.GetProperty("paidAmount").GetDecimal()).ShouldBe((false, 0m));
    }

    [Test]
    public async Task AReset_EmptiesTheSettlements_AndSetsTheModeToPay()
    {
        await SetSettlementModeAsync("Hold");
        await SellAsync(TrustUsd);
        await SettleAsync();

        (await _control.PostJsonAsync("/control/reset", """{ "seed": 7 }""")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await BrokerHttp.SettlementsAsync()).ShouldBeEmpty();
        (await BrokerHttp.SettingsAsync(_control)).GetProperty("settlementMode").GetString().ShouldBe("Pay");
    }

    [TestCase("Currency", "usd")]
    [TestCase("AccountNumber", "")]
    [TestCase("AccountNumber", "BRK_SIM")]
    [TestCase("Name", "")]
    public void ABadBrokerBankAccount_StopsTheHostBeforeItListens(string field, string value)
    {
        var settings = new Dictionary<string, string?>(BrokerAccounts) { [$"Simulator:Broker:BankAccounts:0:{field}"] = value };

        using var host = new SimulatorFactory(settings: settings);

        Should.Throw<Exception>(() => host.Server).ToString().ShouldContain("Simulator:Broker:BankAccounts");
    }

    /// <summary>A sell of 10 at 99.00 against <paramref name="account"/>: the broker owes 990.00.</summary>
    private Task<string> SellAsync(string account, string clientOrderId = OrderId) =>
        BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(
            clientOrderId, side: "Sell", quantity: 10m, priceLimit: 95m, referencePrice: 99m, settlementAccount: account));

    /// <summary>To the settlement date, then any broker route brings the books up to date.</summary>
    private async Task SettleAsync()
    {
        await MarketHttp.AdvanceAsync(_control, "P4D");
        await BrokerHttp.SettingsAsync(_control);
    }

    private static async Task RefusedAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ErrorKeysAsync()).ShouldBe([field]);
    }

    private Task<HttpResponseMessage> SetSettlementModeAsync(string mode) =>
        _control.PostJsonAsync("/control/broker/settlement-mode", JsonSerializer.Serialize(new { mode }));

    private Task<HttpResponseMessage> PayAsync(string settlementId, decimal? amount) =>
        _control.PostJsonAsync($"/control/broker/settlements/{settlementId}/pay", JsonSerializer.Serialize(new { amount }));

    private async Task<List<JsonElement>> SettlementsAsync() =>
        (await BrokerHttp.GetAsync(_control, "/control/broker/settlements")).GetProperty("items").EnumerateArray().ToList();
}
