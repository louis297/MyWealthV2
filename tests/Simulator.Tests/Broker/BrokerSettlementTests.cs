using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Broker;

/// <summary>Trade-settlement R35, R36, R41: an order's settlement account, the broker settlements of its fills and the
/// <c>Settlement</c> report on the feed.</summary>
public class BrokerSettlementTests
{
    private const int Seed = 42;
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";
    private const string OtherOrderId = "7d1f3a55-0e0b-4c1e-8a57-5b7f0c2d9e42";
    private const string ThirdOrderId = "a3e0f6c2-1b9d-4c7e-9f3a-2d5b8e1c7a90";
    private const string TrustUsd = "SIM-DEMO-USD";

    // TestClock.Start is Thursday 2026-11-19; two weekdays later is Monday 2026-11-23.
    private static readonly DateOnly Monday = new(2026, 11, 23);

    private SimulatorFactory _factory = null!;
    private HttpClient _control = null!;
    private HttpClient _party = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new SimulatorFactory();
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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, Bank.BankHttp.ResetBank);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, $"UPDATE [World] SET [Seed] = {Seed};");
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);
    }

    [Test]
    public async Task Script0011_IsApplied()
    {
        (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0011_broker_settlement.sql';")).Single()[0].ShouldBe(1);
    }

    [Test]
    public async Task OnTheSettlementDate_TheFillsOfASettlementAccountAndCurrency_SettleInOneBrokerSettlement_WithOneSettlementReport()
    {
        var prefix = (await MarketHttp.WorldIdAsync()).ToString("N");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(quantity: 4m, referencePrice: 100.12m, settlementAccount: TrustUsd));
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId, quantity: 6m, referencePrice: 100.17m, settlementAccount: TrustUsd));

        await MarketHttp.AdvanceAsync(_control, "P4D");
        var (items, _, _) = await BrokerHttp.FeedAsync(_party);

        var settlementId = $"{prefix[..8]}-S1";
        items.Select(item => item.GetProperty("kind").GetString()).ShouldBe(["Fill", "Fill", "Settled", "Settled", "Settlement"]);
        items[2].GetProperty("settlementId").GetString().ShouldBe(settlementId);
        items[3].GetProperty("settlementId").GetString().ShouldBe(settlementId);
        items[0].GetProperty("settlementId").ValueKind.ShouldBe(JsonValueKind.Null);
        var closing = items[4];
        closing.GetProperty("id").GetString().ShouldBe($"{prefix}-X5");
        (closing.GetProperty("settlementId").GetString(), closing.GetProperty("settlementAccount").GetString(),
                closing.GetProperty("currency").GetString(), closing.GetProperty("settledDate").GetString())
            .ShouldBe((settlementId, TrustUsd, "USD", "2026-11-23"));
        closing.GetProperty("netAmount").ValueKind.ShouldBe(JsonValueKind.Number);
        (closing.GetProperty("netAmount").GetDecimal(), closing.GetProperty("fillCount").GetInt32()).ShouldBe((-1001.50m, 2));
        foreach (var name in new[] { "clientOrderId", "brokerOrderId", "quantity", "price", "fillId", "reason" })
        {
            closing.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null, name);
        }

        (await BrokerHttp.SettlementsAsync()).ShouldBe([new SettlementRow(1, TrustUsd, "USD", Monday, -1001.50m, 2, 0m)]);
    }

    [Test]
    public async Task ASellAndABuy_NetInOneSettlement_AndAnotherCurrencyOrAccount_IsItsOwnSettlement()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(quantity: 4m, referencePrice: 100m, settlementAccount: TrustUsd));
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(
            OtherOrderId, side: "Sell", quantity: 10m, priceLimit: 95m, referencePrice: 99m, settlementAccount: TrustUsd));
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(
            ThirdOrderId, currency: "NZD", quantity: 1m, referencePrice: 50m, settlementAccount: "SIM-DEMO-NZD"));

        await MarketHttp.AdvanceAsync(_control, "P4D");
        await BrokerHttp.SettingsAsync(_control);

        (await BrokerHttp.SettlementsAsync()).ShouldBe([
            new SettlementRow(1, TrustUsd, "USD", Monday, 590m, 2, 0m),
            new SettlementRow(2, "SIM-DEMO-NZD", "NZD", Monday, -50m, 1, 0m)
        ]);
    }

    [Test]
    public async Task AnOrderWithoutASettlementAccount_SettlesAsBefore_WithNoSettlementId_AndNoSettlementReport()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        await MarketHttp.AdvanceAsync(_control, "P4D");
        var (items, _, _) = await BrokerHttp.FeedAsync(_party);

        items.Select(item => item.GetProperty("kind").GetString()).ShouldBe(["Fill", "Settled"]);
        items[1].GetProperty("settlementId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await BrokerHttp.SettlementsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task TheSettlementAccount_IsStored_AndIsPartOfTheIdempotencyCheckOnlyWhenItIsSent()
    {
        var first = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(settlementAccount: TrustUsd));
        var again = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(settlementAccount: TrustUsd));
        var other = await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order(settlementAccount: "SIM-DEMO-NZD"));
        var plain = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));
        var plainAgain = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));

        again.ShouldBe(first);
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        plainAgain.ShouldBe(plain);
        (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
                "SELECT [SettlementAccount] FROM [BrokerOrders] ORDER BY [Number]"))
            .Select(row => row[0] is DBNull ? null : (string)row[0]).ShouldBe([TrustUsd, null]);
    }

    [TestCase("")]
    [TestCase("SIM  DEMO")]
    [TestCase("SIM_DEMO")]
    [TestCase("12345678901234567890123456789012345")]
    public async Task ASettlementAccountOutsideTheShapeOfAnAccountNumber_Returns400(string account)
    {
        (await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order(settlementAccount: account))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
