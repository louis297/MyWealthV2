using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Broker;

/// <summary>Trade-settlement R40: the operator breaks a fill; the broker appends a <c>Broken</c> report.</summary>
public class BrokerBreakTests
{
    private const int Seed = 42;
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";

    private SimulatorFactory _factory = null!;
    private HttpClient _control = null!;
    private HttpClient _party = null!;
    private string _prefix = "";

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
        _prefix = (await MarketHttp.WorldIdAsync()).ToString("N");
    }

    [Test]
    public async Task BreakingAFillOfAWorkingOrder_AppendsABrokenReport_AndTheOrderTakesTheQuantityBack_AndStaysWorking()
    {
        await BrokerHttp.SetModeOkAsync(_control, "Hold");
        var brokerOrderId = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        (await BrokerHttp.FillAsync(_control, brokerOrderId, 4m)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await BreakAsync($"{_prefix}-X1", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var order = (await BrokerHttp.OrdersAsync()).Single();
        (order.Status, order.FilledQuantity).ShouldBe((0, 0m));
        var (items, _, _) = await BrokerHttp.FeedAsync(_party);
        var broken = items[1];
        (broken.GetProperty("kind").GetString(), broken.GetProperty("fillId").GetString(), broken.GetProperty("reason").GetString(),
                broken.GetProperty("clientOrderId").GetString(), broken.GetProperty("brokerOrderId").GetString())
            .ShouldBe(("Broken", $"{_prefix}-X1", "Broken by the exchange", OrderId, brokerOrderId));
        (await BrokerHttp.LogActionsAsync()).ShouldContain("BrokerFillBroken");
        (await BrokerHttp.FillAsync(_control, brokerOrderId, 10m)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task BreakingTheFillOfAFilledOrder_CancelsTheOrder_WithNoReportOfItsOwn_AndKeepsTheReason()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        (await BreakAsync($"{_prefix}-X1", "Exchange error")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await BrokerHttp.OrdersAsync()).Single().Status.ShouldBe(2);
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null), new ReportRow(4, 1, 1, null, "Exchange error")]);
    }

    [Test]
    public async Task ABrokenFill_NeverSettles_AndIsInNoSettlement()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(settlementAccount: "SIM-DEMO-USD"));
        (await BreakAsync($"{_prefix}-X1", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await MarketHttp.AdvanceAsync(_control, "P4D");
        await BrokerHttp.SettingsAsync(_control);

        (await BrokerHttp.FillsAsync()).Single().Settled.ShouldBeFalse();
        (await BrokerHttp.ReportsAsync()).Select(report => report.Kind).ShouldBe([0, 4]);
        (await BrokerHttp.SettlementsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task BreakingASettledOrABrokenFill_Returns400AtStatus_AndAnUnknownOne404()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        await MarketHttp.AdvanceAsync(_control, "P4D");
        await BrokerHttp.SettingsAsync(_control);
        await BrokerHttp.SetModeOkAsync(_control, "Hold");
        var held = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("7d1f3a55-0e0b-4c1e-8a57-5b7f0c2d9e42"));
        await BrokerHttp.FillAsync(_control, held, 1m);
        (await BreakAsync($"{_prefix}-X3", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await RefusedAsync(await BreakAsync($"{_prefix}-X1", null), "Status");
        await RefusedAsync(await BreakAsync($"{_prefix}-X3", null), "Status");
        (await BreakAsync($"{_prefix}-X2", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BreakAsync($"{_prefix}-X99", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BreakAsync("not-a-report", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AReasonOver140Characters_OrEmpty_Returns400AtReason()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        await RefusedAsync(await BreakAsync($"{_prefix}-X1", new string('r', 141)), "Reason");
        await RefusedAsync(await BreakAsync($"{_prefix}-X1", ""), "Reason");
        (await BrokerHttp.FillsAsync()).Single().Settled.ShouldBeFalse();
    }

    private Task<HttpResponseMessage> BreakAsync(string fillReportId, string? reason) =>
        _control.PostJsonAsync($"/control/broker/fills/{fillReportId}/break", JsonSerializer.Serialize(new { reason }));

    private static async Task RefusedAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ErrorKeysAsync()).ShouldBe([field]);
    }
}
