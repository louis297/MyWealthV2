using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>Deposit matching R38, R40–R42, R46, R47: the simulator bank's control routes.</summary>
public class BankControlTests
{
    private SimulatorFactory _factory = null!;
    private HttpClient _control = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new SimulatorFactory();
        _control = _factory.SessionClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        _control.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public async Task ResetState()
    {
        _factory.Clock.Set(TestClock.Start);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BankHttp.ResetBank);
    }

    [Test]
    public async Task Script0003_IsApplied()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0003_bank.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task OpenAccount_Returns201WithTheAccount_AndLogsTheAction()
    {
        var response = await BankHttp.OpenAsync(_control, " SIM-DEMO-USD ", "USD", " Demo Firm Client Funds Trust Account ");

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = await response.ReadJsonAsync();
        json.RootElement.GetProperty("accountNumber").GetString().ShouldBe("SIM-DEMO-USD");
        json.RootElement.GetProperty("currency").GetString().ShouldBe("USD");
        json.RootElement.GetProperty("name").GetString().ShouldBe("Demo Firm Client Funds Trust Account");
        json.RootElement.GetProperty("balance").GetDecimal().ShouldBe(0m);
        (await LogActionsAsync()).ShouldBe(["BankAccountOpened"]);
    }

    [Test]
    public async Task OpenAccount_ANumberAlreadyOpen_InAnyCase_Returns400AndLogsNothing()
    {
        await BankHttp.OpenOkAsync(_control, "SIM-DEMO-USD");

        var response = await BankHttp.OpenAsync(_control, "sim-demo-usd", "USD");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe(["AccountNumber"]);
        (await LogActionsAsync()).ShouldBe(["BankAccountOpened"]);
    }

    [TestCase("", "USD", "Trust", "AccountNumber")]
    [TestCase("12345678901234567890123456789012345", "USD", "Trust", "AccountNumber")]
    [TestCase("02/0100", "USD", "Trust", "AccountNumber")]
    [TestCase("GB29  NWBK", "USD", "Trust", "AccountNumber")]
    [TestCase("SIM-1", "usd", "Trust", "Currency")]
    [TestCase("SIM-1", "US", "Trust", "Currency")]
    [TestCase("SIM-1", "USD", "", "Name")]
    public async Task OpenAccount_AFieldOutsideItsRule_Returns400AtThatField(string number, string currency, string name, string field)
    {
        var response = await BankHttp.OpenAsync(_control, number, currency, name);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldContain(field);
        (await LogActionsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task OpenAccount_ANameOver140_Returns400()
    {
        var response = await BankHttp.OpenAsync(_control, "SIM-1", "USD", new string('N', 141));

        (await response.ErrorKeysAsync()).ShouldContain("Name");
    }

    [Test]
    public async Task AddReceipt_Returns201WithTheOutsideIdOfTheWorld_AndLogsTheBody()
    {
        await BankHttp.OpenOkAsync(_control, "SIM-DEMO-USD");
        var world = await (await _control.GetAsync("/control/world")).ReadWorldAsync();

        var first = await BankHttp.AddReceiptOkAsync(_control, new
        {
            accountNumber = "SIM-DEMO-USD",
            amount = 500.00m,
            reference = "K7M 3Q9 PXB",
            payerName = "J SMITH",
            payerAccount = "12-3456-7890042-00"
        });
        var second = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "sim-demo-usd", amount = 1m });

        first.ShouldBe($"{world.WorldId:N}-L1");
        second.ShouldBe($"{world.WorldId:N}-L2");
        (await LogActionsAsync()).ShouldBe(["BankReceiptAdded", "BankReceiptAdded", "BankAccountOpened"]);
        var detail = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT TOP 1 [Detail] FROM [ControlLog] WHERE [Action] = N'BankReceiptAdded' ORDER BY [Id];");
        ((string)detail.Single()[0]).ShouldContain("K7M 3Q9 PXB");
    }

    [Test]
    public async Task AddReceipt_ToAnAccountNotOpen_Returns400AtAccountNumber()
    {
        var response = await BankHttp.AddReceiptAsync(_control, new { accountNumber = "SIM-NONE", amount = 1m });

        (await response.ErrorKeysAsync()).ShouldBe(["AccountNumber"]);
        (await LogActionsAsync()).ShouldBeEmpty();
    }

    private static IEnumerable<TestCaseData> BadReceipts()
    {
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 0m }, "Amount");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = -1m }, "Amount");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1.00001m }, "Amount");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1m, reference = new string('R', 141) }, "Reference");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1m, payerName = new string('P', 141) }, "PayerName");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1m, payerAccount = new string('1', 35) }, "PayerAccount");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1m, valueDate = "2026-11-20" }, "ValueDate");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1m, availableAfter = "P31D" }, "AvailableAfter");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1m, availableAfter = "soon" }, "AvailableAfter");
        yield return new TestCaseData(new { accountNumber = "SIM-1", amount = 1m, availableAfter = "-PT1H" }, "AvailableAfter");
    }

    [TestCaseSource(nameof(BadReceipts))]
    public async Task AddReceipt_AFieldOutsideItsRule_Returns400AtThatField_AndWritesNothing(object body, string field)
    {
        await BankHttp.OpenOkAsync(_control, "SIM-1");

        var response = await BankHttp.AddReceiptAsync(_control, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldContain(field);
        (await LogActionsAsync()).ShouldBe(["BankAccountOpened"]);
        ((int)(await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT COUNT(*) FROM [BankLines]")).Single()[0]).ShouldBe(0);
    }

    [Test]
    public async Task AddReceipt_ValueDateDefaultsToTheSimulatorDate_AndMayBeEarlier()
    {
        await BankHttp.OpenOkAsync(_control, "SIM-1");
        await _control.PostJsonAsync("/control/clock/advance", """{ "by": "P2D" }""");

        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 1m });
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 2m, valueDate = "2026-11-01" });

        var lines = await LinesAsync("SIM-1");
        lines.Select(line => line.GetProperty("valueDate").GetString()).ShouldBe(["2026-11-21", "2026-11-01"]);
    }

    [Test]
    public async Task Accounts_ShowTheBalanceOfEveryLine_PlacedOrNot()
    {
        await BankHttp.OpenOkAsync(_control, "SIM-1");
        await BankHttp.OpenOkAsync(_control, "SIM-2", "NZD");
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 10.5m });
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 4m, availableAfter = "P1D" });

        var accounts = await BankHttp.AccountsAsync(_control);

        var items = accounts.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("accountNumber").GetString()).ShouldBe(["SIM-1", "SIM-2"]);
        items[0].GetProperty("balance").GetDecimal().ShouldBe(14.5m);
        items[1].GetProperty("balance").GetDecimal().ShouldBe(0m);
        items[1].GetProperty("currency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task StatementLines_ListEveryLineWithItsAvailableAtAndPlaces()
    {
        await BankHttp.OpenOkAsync(_control, "SIM-1");
        var later = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 1m, availableAfter = "PT1H" });
        var twice = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 2m, deliverTwice = true });
        using var party = _factory.PartyClient();
        await BankHttp.FeedOkAsync(party, "SIM-1");

        var lines = await LinesAsync("SIM-1");

        var byId = lines.ToDictionary(line => line.GetProperty("id").GetString()!);
        byId[later].GetProperty("availableAt").GetDateTimeOffset().ShouldBe(TestClock.Start.AddHours(1));
        byId[later].GetProperty("places").GetArrayLength().ShouldBe(0);
        byId[twice].GetProperty("places").GetArrayLength().ShouldBe(2);
        byId[twice].GetProperty("direction").GetString().ShouldBe("Credit");
        byId[twice].GetProperty("amount").GetDecimal().ShouldBe(2m);
    }

    [Test]
    public async Task UnknownAccount_StatementLines_Returns404()
    {
        (await _control.GetAsync("/control/bank/accounts/SIM-NONE/statement-lines")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Reset_EmptiesTheBank_AndOutsideIdsNeverRepeat()
    {
        await BankHttp.OpenOkAsync(_control, "SIM-1");
        var before = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 1m });

        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await BankHttp.AccountsAsync(_control)).GetProperty("items").GetArrayLength().ShouldBe(0);
        ((int)(await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT COUNT(*) FROM [BankLines]")).Single()[0]).ShouldBe(0);
        await BankHttp.OpenOkAsync(_control, "SIM-1");
        var after = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-1", amount = 1m });
        after.ShouldNotBe(before);
        after.ShouldEndWith("-L1");
        var world = await (await _control.GetAsync("/control/world")).ReadWorldAsync();
        after.ShouldStartWith(world.WorldId.ToString("N"));
    }

    [Test]
    public async Task BankRoutes_NeedTheOperator()
    {
        using var anonymous = _factory.ControlClient();

        (await anonymous.GetAsync("/control/bank/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await BankHttp.OpenAsync(anonymous, "SIM-1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<List<JsonElement>> LinesAsync(string accountNumber)
    {
        var response = await _control.GetAsync($"/control/bank/accounts/{accountNumber}/statement-lines");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static async Task<List<string>> LogActionsAsync() =>
        (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [Action] FROM [ControlLog] ORDER BY [Id] DESC"))
        .Select(row => (string)row[0]).ToList();
}
