using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>Interest-run R43, R44 (S1 trust interest): the bank pays interest on a trust account (script 0007).</summary>
public class BankInterestTests
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
    public async Task Script0007_IsApplied()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0007_bank_line_kind.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task Interest_IsACreditLineOfKindInterest_FromBankInterest_CountedInTheBalance_AndLogged()
    {
        var receipt = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = Account, amount = 100m, payerName = "J SMITH" });

        var id = await AddInterestOkAsync(new { accountNumber = Account, amount = 520.00m, reference = "Interest December" });

        var page = await BankHttp.FeedOkAsync(_party, Account);
        page.Ids.ShouldBe([receipt, id]);
        var line = page.Items.Single(item => item.GetProperty("id").GetString() == id);
        line.GetProperty("kind").GetString().ShouldBe("Interest");
        line.GetProperty("direction").GetString().ShouldBe("Credit");
        line.GetProperty("amount").GetDecimal().ShouldBe(520m);
        line.GetProperty("payerName").GetString().ShouldBe("Bank interest");
        line.GetProperty("payerAccount").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("reference").GetString().ShouldBe("Interest December");
        line.GetProperty("endToEndId").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("returnOf").ValueKind.ShouldBe(JsonValueKind.Null);
        page.Items.Single(item => item.GetProperty("id").GetString() == receipt).TryGetProperty("kind", out _).ShouldBeFalse();
        page.Balance.ShouldBe(620m);
        (await BankHttp.AccountsAsync(_control)).GetProperty("items")[0].GetProperty("balance").GetDecimal().ShouldBe(620m);
        (await LogActionsAsync()).ShouldBe(["BankInterestPaid", "BankReceiptAdded", "BankAccountOpened"]);
    }

    [Test]
    public async Task TheDefaultReference_IsInterest()
    {
        await AddInterestOkAsync(new { accountNumber = Account, amount = 1m });

        (await BankHttp.FeedOkAsync(_party, Account)).Items.Single().GetProperty("reference").GetString().ShouldBe("Interest");
    }

    [Test]
    public async Task TheControlStatementLines_ShowTheKindOfEachLine()
    {
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = Account, amount = 100m, payerName = "J SMITH" });
        await AddInterestOkAsync(new { accountNumber = Account, amount = 2m });

        var response = await _control.GetAsync($"/control/bank/accounts/{Account}/statement-lines");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("kind").GetString())
            .ShouldBe(["Transfer", "Interest"]);
    }

    [Test]
    public async Task InterestLeftOutOfTheFeed_IsNeverAnItem()
    {
        await AddInterestOkAsync(new { accountNumber = Account, amount = 5m, omitFromFeed = true });

        var page = await BankHttp.FeedOkAsync(_party, Account);
        page.Items.ShouldBeEmpty();
        page.Balance.ShouldBe(5m);
    }

    private static IEnumerable<TestCaseData> BadInterest()
    {
        yield return new TestCaseData(new { accountNumber = "SIM-NONE", amount = 15m }, "AccountNumber");
        yield return new TestCaseData(new { accountNumber = Account, amount = 0m }, "Amount");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1.00001m }, "Amount");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1m, reference = new string('r', 141) }, "Reference");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1m, availableAfter = "P31D" }, "AvailableAfter");
        yield return new TestCaseData(new { accountNumber = Account, amount = 1m, valueDate = "2099-01-01" }, "ValueDate");
    }

    [TestCaseSource(nameof(BadInterest))]
    public async Task InterestOutsideItsRules_Returns400AtThatField_AndWritesNothing(object body, string field)
    {
        var response = await AddInterestAsync(body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldContain(field);
        (await LogActionsAsync()).ShouldBe(["BankAccountOpened"]);
        var lines = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT COUNT(*) FROM [BankLines];");
        ((int)lines.Single()[0]).ShouldBe(0);
    }

    private Task<HttpResponseMessage> AddInterestAsync(object body) =>
        _control.PostJsonAsync("/control/bank/interest", JsonSerializer.Serialize(body));

    private async Task<string> AddInterestOkAsync(object body)
    {
        var response = await AddInterestAsync(body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<List<string>> LogActionsAsync() =>
        (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [Action] FROM [ControlLog] ORDER BY [Id] DESC"))
        .Select(row => (string)row[0]).ToList();
}
