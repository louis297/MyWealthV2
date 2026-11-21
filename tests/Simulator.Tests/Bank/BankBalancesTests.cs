using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>Bank-reconciliation R26–R29 (S1 balances): the balance on the feed, bank charges, lines left out of the
/// feed and the account's statement balance (script 0006).</summary>
public class BankBalancesTests
{
    private const string Account = "SIM-DEMO-USD";

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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BankHttp.ResetBank);
        await BankHttp.OpenOkAsync(_control, Account);
    }

    [Test]
    public async Task Script0006_IsApplied()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0006_bank_balances.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task Feed_BeforeTheFirstPlace_HasBalanceZero_AsANumber()
    {
        var response = await BankHttp.FeedAsync(_party, Account);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("balance").ValueKind.ShouldBe(JsonValueKind.Number);
        json.RootElement.GetProperty("balance").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task Balance_IsTheSignedSumOfTheLinesUpToTheCursor_AfterReceiptsPaymentsAndRecalls()
    {
        var receipt = await ReceiptAsync(500m);
        await ReceiptAsync(100.25m);
        (await BankHttp.PayAsync(_party, Account, BankHttp.Payment(amount: 300m))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _control.PostJsonAsync($"/control/bank/lines/{Uri.EscapeDataString(receipt)}/recall",
            JsonSerializer.Serialize(new { reason = "Recalled" }))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var first = await BankHttp.FeedOkAsync(_party, Account, limit: 1);
        var all = await BankHttp.FeedOkAsync(_party, Account);

        first.Balance.ShouldBe(500m);
        all.Items.Count.ShouldBe(4);
        all.Balance.ShouldBe(500m + 100.25m - 300m - 500m);
    }

    [Test]
    public async Task ALineDeliveredTwice_IsCountedOnce()
    {
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = Account, amount = 40m, deliverTwice = true });

        var page = await BankHttp.FeedOkAsync(_party, Account);

        page.Items.Count.ShouldBe(2);
        page.Balance.ShouldBe(40m);
    }

    [Test]
    public async Task AnEmptyPage_CarriesTheBalanceAtItsCursor()
    {
        await ReceiptAsync(70m);
        var page = await BankHttp.FeedOkAsync(_party, Account);

        var next = await BankHttp.FeedOkAsync(_party, Account, after: page.Cursor);

        next.Items.ShouldBeEmpty();
        next.Cursor.ShouldBe(page.Cursor);
        next.Balance.ShouldBe(70m);
    }

    [Test]
    public async Task ACharge_IsADebitLineWithNoPayeeOrIds_AndIsLogged()
    {
        var id = await BankHttp.AddChargeOkAsync(_control, new
        {
            accountNumber = Account,
            amount = 15m,
            reference = "Account fee November",
            valueDate = TestClock.Start.ToString("yyyy-MM-dd")
        });

        var page = await BankHttp.FeedOkAsync(_party, Account);

        var line = page.Items.ShouldHaveSingleItem();
        line.GetProperty("id").GetString().ShouldBe(id);
        line.GetProperty("direction").GetString().ShouldBe("Debit");
        line.GetProperty("amount").GetDecimal().ShouldBe(15m);
        line.GetProperty("reference").GetString().ShouldBe("Account fee November");
        line.GetProperty("payerName").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("payerAccount").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("endToEndId").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("returnOf").ValueKind.ShouldBe(JsonValueKind.Null);
        page.Balance.ShouldBe(-15m);
        (await LogActionsAsync()).ShouldBe(["BankChargeAdded", "BankAccountOpened"]);
    }

    [Test]
    public async Task AChargeAfterAvailableAfter_IsNotInTheFeedUntilThen()
    {
        await BankHttp.AddChargeOkAsync(_control, new { accountNumber = Account, amount = 5m, availableAfter = "PT1H" });

        (await BankHttp.FeedOkAsync(_party, Account)).Items.ShouldBeEmpty();
        _factory.Clock.Advance(TimeSpan.FromHours(1));
        (await BankHttp.FeedOkAsync(_party, Account)).Items.Count.ShouldBe(1);
    }

    [TestCaseSource(nameof(BadCharges))]
    public async Task AChargeOutsideItsRules_Returns400AtThatField_AndWritesNothing(object body, string field)
    {
        var response = await BankHttp.AddChargeAsync(_control, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldContain(field);
        (await LogActionsAsync()).ShouldBe(["BankAccountOpened"]);
        var lines = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT COUNT(*) FROM [BankLines];");
        ((int)lines.Single()[0]).ShouldBe(0);
    }

    [Test]
    public async Task ALineLeftOutOfTheFeed_IsNeverAnItem_ButCountsInTheBalanceFromItsPlace()
    {
        var first = await ReceiptAsync(100m);
        await BankHttp.AddChargeOkAsync(_control, new { accountNumber = Account, amount = 15m, omitFromFeed = true });
        var omitted = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = Account, amount = 7m, omitFromFeed = true });
        var last = await ReceiptAsync(50m);

        var firstPage = await BankHttp.FeedOkAsync(_party, Account, limit: 1);
        var rest = await BankHttp.FeedOkAsync(_party, Account, after: firstPage.Cursor);

        firstPage.Ids.ShouldBe([first]);
        firstPage.Balance.ShouldBe(100m);
        rest.Ids.ShouldBe([last]);
        rest.Ids.ShouldNotContain(omitted);
        rest.Balance.ShouldBe(100m - 15m + 7m + 50m);
        rest.HasMore.ShouldBeFalse();
    }

    [Test]
    public async Task OnlyLinesLeftOutOfTheFeed_GiveAnEmptyPage_WhoseCursorAndBalancePassThem()
    {
        await BankHttp.AddChargeOkAsync(_control, new { accountNumber = Account, amount = 15m, omitFromFeed = true });

        var page = await BankHttp.FeedOkAsync(_party, Account);
        var next = await BankHttp.FeedOkAsync(_party, Account, after: page.Cursor);

        page.Items.ShouldBeEmpty();
        page.HasMore.ShouldBeFalse();
        page.Balance.ShouldBe(-15m);
        next.Cursor.ShouldBe(page.Cursor);
        next.Balance.ShouldBe(-15m);
    }

    [Test]
    public async Task OmitFromFeed_WithDeliverTwice_Returns400AtOmitFromFeed_AndWritesNothing()
    {
        var response = await BankHttp.AddReceiptAsync(_control,
            new { accountNumber = Account, amount = 5m, omitFromFeed = true, deliverTwice = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe(["OmitFromFeed"]);
        (await LogActionsAsync()).ShouldBe(["BankAccountOpened"]);
    }

    [Test]
    public async Task Accounts_ShowTheStatementBalance_BesideTheBalanceOfEveryLine()
    {
        await ReceiptAsync(100m);
        await BankHttp.AddChargeOkAsync(_control, new { accountNumber = Account, amount = 15m, omitFromFeed = true });
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = Account, amount = 30m, availableAfter = "PT1H" });
        await BankHttp.FeedOkAsync(_party, Account);

        var account = (await BankHttp.AccountsAsync(_control)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("accountNumber").GetString() == Account);

        account.GetProperty("balance").GetDecimal().ShouldBe(115m);
        account.GetProperty("statementBalance").GetDecimal().ShouldBe(85m);
    }

    [Test]
    public async Task StatementLines_ShowOmitFromFeed()
    {
        var shown = await ReceiptAsync(1m);
        var hidden = await BankHttp.AddChargeOkAsync(_control, new { accountNumber = Account, amount = 2m, omitFromFeed = true });

        var response = await _control.GetAsync($"/control/bank/accounts/{Account}/statement-lines");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var byId = json.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("omitFromFeed").GetBoolean());
        byId[shown].ShouldBeFalse();
        byId[hidden].ShouldBeTrue();
    }

    private static IEnumerable<TestCaseData> BadCharges()
    {
        yield return new TestCaseData(new { accountNumber = "SIM-NONE", amount = 15m }, "AccountNumber");
        yield return new TestCaseData(new { accountNumber = Account, amount = 0m }, "Amount");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1.00001m }, "Amount");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1m, reference = new string('r', 141) }, "Reference");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1m, availableAfter = "P31D" }, "AvailableAfter");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1m, valueDate = "2099-01-01" }, "ValueDate");
    }

    private async Task<string> ReceiptAsync(decimal amount) =>
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = Account, amount, payerName = "J SMITH" });

    private static async Task<List<string>> LogActionsAsync() =>
        (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [Action] FROM [ControlLog] ORDER BY [Id] DESC"))
        .Select(row => (string)row[0]).ToList();
}
