using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Broker;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Custodian;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Market;

/// <summary>Dividends R54–R56, R61: the simulator market announces dividends, and a dividend goes ex.</summary>
public class MarketDividendTests
{
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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, Bank.BankHttp.ResetBank);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m, dailyMovePercent: 0m);
        await CustodianHttp.OpenOkAsync(_control);
    }

    public static Dictionary<string, object?> Dividend(DateOnly exDate, DateOnly? paymentDate = null, decimal? price = 25.00m) => new()
    {
        ["symbol"] = "ACME.SIM",
        ["kind"] = "Dividend",
        ["amountPerShare"] = 1.00m,
        ["taxSource"] = "Foreign",
        ["imputationCreditPerShare"] = 0m,
        ["foreignTaxRatePercent"] = 15m,
        ["reinvestmentPrice"] = price,
        ["exDate"] = exDate.ToString("yyyy-MM-dd"),
        ["paymentDate"] = (paymentDate ?? exDate.AddDays(4)).ToString("yyyy-MM-dd")
    };

    private Task<HttpResponseMessage> AnnounceAsync(Dictionary<string, object?> body) =>
        _control.PostJsonAsync("/control/market/corporate-actions", JsonSerializer.Serialize(body));

    private async Task<string> AnnounceOkAsync(Dictionary<string, object?> body)
    {
        var response = await AnnounceAsync(body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    [Test]
    public async Task ADividend_IsAnnouncedWithItsTermsAndNoRatio_AndGoesOnTheFeed()
    {
        var id = await AnnounceOkAsync(Dividend(Day0.AddDays(2)));

        var item = await MarketHttp.CorporateActionAsync(_control, id);
        item.GetProperty("kind").GetString().ShouldBe("Dividend");
        item.GetProperty("ratioNew").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("amountPerShare").GetDecimal().ShouldBe(1.00m);
        item.GetProperty("taxSource").GetString().ShouldBe("Foreign");
        item.GetProperty("foreignTaxRatePercent").GetDecimal().ShouldBe(15m);
        item.GetProperty("reinvestmentPrice").GetDecimal().ShouldBe(25.00m);

        var feed = await _party.GetAsync("/party/market/corporate-actions");
        using var page = JsonDocument.Parse(await feed.Content.ReadAsStringAsync());
        var version = page.RootElement.GetProperty("items").EnumerateArray().Single();
        (version.GetProperty("kind").GetString(), version.GetProperty("version").GetInt32()).ShouldBe(("Dividend", 1));
        version.GetProperty("ratioNew").ValueKind.ShouldBe(JsonValueKind.Null);
        version.GetProperty("ratioHeld").ValueKind.ShouldBe(JsonValueKind.Null);
        version.GetProperty("amountPerShare").GetDecimal().ShouldBe(1.00m);
        version.GetProperty("taxSource").GetString().ShouldBe("Foreign");
        version.GetProperty("imputationCreditPerShare").GetDecimal().ShouldBe(0m);
        version.GetProperty("foreignTaxRatePercent").GetDecimal().ShouldBe(15m);
        version.GetProperty("reinvestmentPrice").GetDecimal().ShouldBe(25.00m);
    }

    [TestCase("ratioNew", 2, "RatioNew")]
    [TestCase("amountPerShare", 0, "AmountPerShare")]
    [TestCase("taxSource", "Mars", "TaxSource")]
    [TestCase("imputationCreditPerShare", 0.1, "ImputationCreditPerShare")]
    [TestCase("foreignTaxRatePercent", 100, "ForeignTaxRatePercent")]
    [TestCase("reinvestmentPrice", 0, "ReinvestmentPrice")]
    [TestCase("paymentDate", null, "PaymentDate")]
    public async Task TermsOutsideTheRule_Return400AtTheField(string field, object? value, string error)
    {
        var body = Dividend(Day0.AddDays(2));
        body[field] = value;

        (await (await AnnounceAsync(body)).ErrorKeysAsync()).ShouldContain(error);
    }

    [Test]
    public async Task APaymentDateBeforeTheExDate_Returns400()
    {
        var body = Dividend(Day0.AddDays(3), paymentDate: Day0.AddDays(2));

        (await (await AnnounceAsync(body)).ErrorKeysAsync()).ShouldContain("PaymentDate");
    }

    [Test]
    public async Task Amend_ReplacesTheTerms_AndAfterTheExDateIsRefused()
    {
        var id = await AnnounceOkAsync(Dividend(Day0.AddDays(2)));
        var amend = Dividend(Day0.AddDays(2));
        amend.Remove("symbol");
        amend.Remove("kind");
        amend["amountPerShare"] = 1.10m;

        (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/amend", JsonSerializer.Serialize(amend)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MarketHttp.CorporateActionAsync(_control, id)).GetProperty("amountPerShare").GetDecimal().ShouldBe(1.10m);

        await MarketHttp.AdvanceAsync(_control, "P2D");
        (await (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/amend", JsonSerializer.Serialize(amend)))
            .ErrorKeysAsync()).ShouldContain("Status");
    }

    [Test]
    public async Task GoingEx_SettlesTheOpenFills_StoresADuePaymentPerAccountWithAPosition_AndLeavesThePriceAndOrdersAlone()
    {
        await BrokerHttp.SetSettlementDaysOkAsync(_control, 5);
        await BrokerHttp.PlaceOkAsync(_party, Order("Buy", 10m));
        await CustodianHttp.OpenOkAsync(_control, "SIM-EMPTY-USD");
        (await CustodianHttp.AddMovementAsync(_control, new { accountNumber = CustodianHttp.Account, direction = "In", symbol = "ACME.SIM",
            quantity = 100m, omitFromFeed = true })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await AnnounceOkAsync(Dividend(Day0.AddDays(2)));

        await MarketHttp.AdvanceAsync(_control, "P2D");

        (await MarketHttp.CorporateActionAsync(_control, id)).GetProperty("status").GetString().ShouldBe("Effective");
        var payments = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, """
            SELECT a.[AccountNumber], p.[EntitledPosition], p.[Status]
            FROM [CustodianDividendPayments] p JOIN [CustodianAccounts] a ON a.[Id] = p.[AccountId]
            """);
        payments.Select(row => ((string)row[0], (decimal)row[1], (int)row[2])).ShouldBe([(CustodianHttp.Account, 110m, 0)]);
        (await MarketHttp.PricesAsync(_control, "ACME.SIM")).ShouldAllBe(price => price.Price == 100m);
        (await CustodianHttp.FeedAsync(_party)).Items.ShouldNotContain(item => item.GetProperty("kind").GetString() == "CorporateAction");
    }

    private static string Order(string side, decimal quantity)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(BrokerHttp.Order(symbol: "ACME.SIM", side: side, quantity: quantity))!;
        body["safekeepingAccount"] = CustodianHttp.Account;
        return JsonSerializer.Serialize(body);
    }
}
