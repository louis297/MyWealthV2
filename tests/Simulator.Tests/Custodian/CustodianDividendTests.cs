using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Bank;
using MyWealthV2.Simulator.Tests.Broker;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Custodian;

/// <summary>Dividends R57, R59, R60, R62, R63: the simulator custodian pays a dividend into an income account.</summary>
public class CustodianDividendTests
{
    private const string Trust = "SIM-DEMO-USD";
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

    private async Task<string> AnnounceAsync(decimal? price = null)
    {
        var response = await _control.PostJsonAsync("/control/market/corporate-actions",
            JsonSerializer.Serialize(MarketDividendTests.Dividend(Day0.AddDays(2), Day0.AddDays(4), price)));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<List<JsonElement>> PaymentsAsync(string query = "") =>
        (await CustodianHttp.GetAsync(_control, "/control/custodian/dividend-payments" + query)).GetProperty("items").EnumerateArray().ToList();

    private async Task<IReadOnlyList<JsonElement>> TrustLinesAsync() => (await BankHttp.FeedOkAsync(_party, Trust)).Items;

    [Test]
    public async Task OnThePaymentDate_TheCashIsPaidIntoTheIncomeAccount_NamingTheDividend()
    {
        var id = await AnnounceAsync();

        await MarketHttp.AdvanceAsync(_control, "P4D");
        await CustodianHttp.FeedAsync(_party);

        var line = (await TrustLinesAsync()).ShouldHaveSingleItem();
        line.GetProperty("amount").GetDecimal().ShouldBe(85.00m);
        line.GetProperty("reference").GetString().ShouldBe(id);
        line.GetProperty("payerName").GetString().ShouldBe("North Nominees");
        line.GetProperty("direction").GetString().ShouldBe("Credit");
        var payment = (await PaymentsAsync()).ShouldHaveSingleItem();
        payment.GetProperty("corporateActionId").GetString().ShouldBe(id);
        payment.GetProperty("accountNumber").GetString().ShouldBe(CustodianHttp.Account);
        payment.GetProperty("entitledPosition").GetDecimal().ShouldBe(100m);
        payment.GetProperty("cash").GetDecimal().ShouldBe(85.00m);
        payment.GetProperty("incomeAccountNumber").GetString().ShouldBe(Trust);
        payment.GetProperty("status").GetString().ShouldBe("Paid");
        payment.GetProperty("bankLineId").GetString().ShouldBe(line.GetProperty("id").GetString());
        (await MarketHttp.CorporateActionAsync(_control, id)).GetProperty("paidOn").GetString().ShouldBe(Day0.AddDays(4).ToString("yyyy-MM-dd"));
    }

    [Test]
    public async Task BeforeThePaymentDate_NothingIsPaid()
    {
        await AnnounceAsync();

        await MarketHttp.AdvanceAsync(_control, "P3D");
        await CustodianHttp.FeedAsync(_party);

        (await TrustLinesAsync()).ShouldBeEmpty();
        (await PaymentsAsync()).Single().GetProperty("status").GetString().ShouldBe("Due");
    }

    [Test]
    public async Task WithNoIncomeAccount_NoCashIsPaid_AndThePaymentSaysSo()
    {
        (await _control.PostJsonAsync($"/control/custodian/accounts/{CustodianHttp.Account}/income-account",
            JsonSerializer.Serialize(new { incomeAccountNumber = (string?)null }))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await AnnounceAsync();

        await MarketHttp.AdvanceAsync(_control, "P4D");
        await CustodianHttp.FeedAsync(_party);

        (await TrustLinesAsync()).ShouldBeEmpty();
        var payment = (await PaymentsAsync()).Single();
        payment.GetProperty("status").GetString().ShouldBe("Paid");
        payment.GetProperty("bankLineId").ValueKind.ShouldBe(JsonValueKind.Null);
        payment.GetProperty("incomeAccountNumber").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TheIncomeAccount_IsSetAndShownOnTheAccount_AndLogged()
    {
        var response = await _control.PostJsonAsync($"/control/custodian/accounts/{CustodianHttp.Account}/income-account",
            JsonSerializer.Serialize(new { incomeAccountNumber = "SIM-OTHER-USD" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var account = (await CustodianHttp.GetAsync(_control, "/control/custodian/accounts")).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("accountNumber").GetString() == CustodianHttp.Account);
        account.GetProperty("incomeAccountNumber").GetString().ShouldBe("SIM-OTHER-USD");
        (await _control.PostJsonAsync("/control/custodian/accounts/NOT-OPEN/income-account",
            JsonSerializer.Serialize(new { incomeAccountNumber = Trust }))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
                "SELECT COUNT(*) FROM [ControlLog] WHERE [Action] = N'CustodianIncomeAccountChanged'")).Single()[0].ShouldBe(1);
    }

    [Test]
    public async Task InHoldMode_APaymentIsHeld_AndPaidLaterWithAnotherAmount()
    {
        (await _control.PostJsonAsync("/control/custodian/income-mode", JsonSerializer.Serialize(new { mode = "Hold" })))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CustodianHttp.GetAsync(_control, "/control/custodian")).GetProperty("incomeMode").GetString().ShouldBe("Hold");
        await AnnounceAsync();
        await MarketHttp.AdvanceAsync(_control, "P4D");
        await CustodianHttp.FeedAsync(_party);
        var held = (await PaymentsAsync("?status=Held")).ShouldHaveSingleItem();
        (await TrustLinesAsync()).ShouldBeEmpty();

        var response = await _control.PostJsonAsync($"/control/custodian/dividend-payments/{held.GetProperty("id").GetString()}/pay",
            JsonSerializer.Serialize(new { amount = 84.99m }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await TrustLinesAsync()).Single().GetProperty("amount").GetDecimal().ShouldBe(84.99m);
        (await PaymentsAsync()).Single().GetProperty("status").GetString().ShouldBe("Paid");
        (await _control.PostJsonAsync($"/control/custodian/dividend-payments/{held.GetProperty("id").GetString()}/pay", "{}"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _control.PostJsonAsync("/control/custodian/income-mode", JsonSerializer.Serialize(new { mode = "Later" })))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AReset_EmptiesThePayments_AndSetsTheIncomeModeBackToPay()
    {
        (await _control.PostJsonAsync("/control/custodian/income-mode", JsonSerializer.Serialize(new { mode = "Hold" })))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await AnnounceAsync();
        await MarketHttp.AdvanceAsync(_control, "P2D");
        await CustodianHttp.FeedAsync(_party);

        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PaymentsAsync()).ShouldBeEmpty();
        (await CustodianHttp.GetAsync(_control, "/control/custodian")).GetProperty("incomeMode").GetString().ShouldBe("Pay");
    }
}
