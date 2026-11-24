using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Bank;
using MyWealthV2.Simulator.Tests.Broker;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Custodian;

/// <summary>Dividends R58, R59: the election route of the simulator custodian and a dividend paid in shares.</summary>
public class CustodianElectionTests
{
    private const string Trust = "SIM-DEMO-USD";
    private const string Instruction = "6b1f0c2e-0000-4000-8000-000000000003";
    private static readonly DateOnly Day0 = DateOnly.FromDateTime(TestClock.Start.UtcDateTime);

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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, CustodianHttp.ResetCustodian);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BrokerHttp.ResetBroker);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BankHttp.ResetBank);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m, dailyMovePercent: 0m);
        await BankHttp.OpenOkAsync(_control, Trust);
        (await _control.PostJsonAsync("/control/custodian/accounts", JsonSerializer.Serialize(new
        {
            accountNumber = CustodianHttp.Account, currency = "USD", name = "North Nominees", incomeAccountNumber = Trust
        }))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CustodianHttp.AddMovementAsync(_control, new { accountNumber = CustodianHttp.Account, direction = "In", symbol = "ACME.SIM",
            quantity = 100m, omitFromFeed = true })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task<string> AnnounceAsync(decimal? price = 25.00m)
    {
        var response = await _control.PostJsonAsync("/control/market/corporate-actions",
            JsonSerializer.Serialize(MarketDividendTests.Dividend(Day0.AddDays(2), Day0.AddDays(4), price)));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static string Election(string corporateActionId, decimal quantity = 78.82352941m, string instructionId = Instruction,
        string symbol = "ACME.SIM") =>
        JsonSerializer.Serialize(new { instructionId, corporateActionId, symbol, currency = "USD", quantity });

    private Task<HttpResponseMessage> ElectAsync(string body, string accountNumber = CustodianHttp.Account) =>
        _party.PostJsonAsync($"/party/custodian/accounts/{Uri.EscapeDataString(accountNumber)}/elections", body);

    private async Task<(string Status, string? Reason)> AnswerAsync(string body)
    {
        var response = await ElectAsync(body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return (json.RootElement.GetProperty("status").GetString()!,
            json.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null);
    }

    private async Task PayAsync()
    {
        await MarketHttp.AdvanceAsync(_control, "P2D");
        await CustodianHttp.FeedAsync(_party);
    }

    [Test]
    public async Task AnAcceptedElection_IsPaidInShares_AndTheRestInCash()
    {
        var id = await AnnounceAsync();
        await MarketHttp.AdvanceAsync(_control, "P2D");
        await CustodianHttp.FeedAsync(_party);

        (await AnswerAsync(Election(id))).ShouldBe(("Accepted", (string?)null));
        await PayAsync();

        var movement = (await CustodianHttp.FeedAsync(_party)).Items.Single(item => item.GetProperty("kind").GetString() == "CorporateAction");
        (movement.GetProperty("direction").GetString(), movement.GetProperty("quantity").GetDecimal()).ShouldBe(("In", 2.68m));
        movement.GetProperty("corporateActionId").GetString().ShouldBe(id);
        movement.GetProperty("settlementDate").GetString().ShouldBe(Day0.AddDays(4).ToString("yyyy-MM-dd"));
        (await BankHttp.FeedOkAsync(_party, Trust)).Items.Single().GetProperty("amount").GetDecimal().ShouldBe(18.00m);
        var payment = (await CustodianHttp.GetAsync(_control, "/control/custodian/dividend-payments")).GetProperty("items")[0];
        (payment.GetProperty("electedQuantity").GetDecimal(), payment.GetProperty("shares").GetDecimal(), payment.GetProperty("cash").GetDecimal())
            .ShouldBe((78.82352941m, 2.68m, 18.00m));
        payment.GetProperty("movementId").GetString().ShouldBe(movement.GetProperty("id").GetString());
        (await CustodianHttp.PositionsAsync(_control))["ACME.SIM"].ShouldBe(102.68m);
    }

    [Test]
    public async Task InWholeShares_TheCustodianIssuesWholeShares_AndPaysTheRestInCash()
    {
        (await CustodianHttp.SetRoundingModeAsync(_control, "WholeShares")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = await AnnounceAsync();
        await MarketHttp.AdvanceAsync(_control, "P2D");
        await CustodianHttp.FeedAsync(_party);
        await AnswerAsync(Election(id));

        await PayAsync();

        (await CustodianHttp.PositionsAsync(_control))["ACME.SIM"].ShouldBe(102m);
        // 85.00 less 2 shares × 25.00.
        (await BankHttp.FeedOkAsync(_party, Trust)).Items.Single().GetProperty("amount").GetDecimal().ShouldBe(35.00m);
    }

    [Test]
    public async Task AnElection_IsRejectedWithTheFirstReasonThatApplies()
    {
        var plan = await AnnounceAsync();
        var cash = await AnnounceAsyncOther();

        (await AnswerAsync(Election(plan, instructionId: "early"))).ShouldBe(("Rejected", "NotEntitled"));
        await MarketHttp.AdvanceAsync(_control, "P2D");
        await CustodianHttp.FeedAsync(_party);

        (await AnswerAsync(Election("not-a-dividend", instructionId: "a"))).ShouldBe(("Rejected", "UnknownCorporateAction"));
        (await AnswerAsync(Election(plan, symbol: "BETA.SIM", instructionId: "b"))).ShouldBe(("Rejected", "UnknownCorporateAction"));
        (await AnswerAsync(Election(cash, instructionId: "c"))).ShouldBe(("Rejected", "NoPlan"));
        (await AnswerAsync(Election(plan, quantity: 100.00000001m, instructionId: "d"))).ShouldBe(("Rejected", "NotEntitled"));

        await PayAsync();
        (await AnswerAsync(Election(plan, instructionId: "e"))).ShouldBe(("Rejected", "TooLate"));
    }

    [Test]
    public async Task TheSameInstruction_GetsTheSameAnswer_AndAnotherBodyIs409()
    {
        var id = await AnnounceAsync();
        await MarketHttp.AdvanceAsync(_control, "P2D");
        await CustodianHttp.FeedAsync(_party);

        (await AnswerAsync(Election(id))).ShouldBe(("Accepted", (string?)null));
        (await AnswerAsync(Election(id))).ShouldBe(("Accepted", (string?)null));
        (await ElectAsync(Election(id, quantity: 50m))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await AnswerAsync(Election("unknown", instructionId: "r1"))).ShouldBe(("Rejected", "UnknownCorporateAction"));
        (await AnswerAsync(Election("unknown", instructionId: "r1"))).ShouldBe(("Rejected", "UnknownCorporateAction"));
        (await ElectAsync(Election(id, instructionId: "r1"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task AnUnknownAccount_Is404_AndABodyOutsideTheRules400()
    {
        var id = await AnnounceAsync();

        (await ElectAsync(Election(id), "NOT-AT-THE-CUSTODIAN")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ElectAsync(Election(id, quantity: 0m))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ElectAsync(Election(id, instructionId: ""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<string> AnnounceAsyncOther()
    {
        await MarketHttp.AddOkAsync(_control, "CASH.SIM", "USD", 10m, dailyMovePercent: 0m);
        var body = MarketDividendTests.Dividend(Day0.AddDays(2), Day0.AddDays(4), price: null);
        body["symbol"] = "CASH.SIM";
        var response = await _control.PostJsonAsync("/control/market/corporate-actions", JsonSerializer.Serialize(body));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }
}
