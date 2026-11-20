using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>Withdrawals R48–R57: the simulator bank's payments side.</summary>
public class BankPaymentsTests
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
    public async Task Script0004_IsApplied()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0004_bank_payments.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task WithoutTheKey_Returns401_AndDuringAnOutage_503()
    {
        using var keyless = _factory.PartyClient(apiKey: null);
        (await BankHttp.PayAsync(keyless, Account, BankHttp.Payment())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await _control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BankHttp.PayAsync(_party, Account, BankHttp.Payment())).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task AnUnknownTrustAccount_Returns404()
    {
        (await BankHttp.PayAsync(_party, "SIM-NOPE", BankHttp.Payment())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestCase("instructionId", "")]
    [TestCase("instructionId", "101")]
    [TestCase("amount", "0")]
    [TestCase("amount", "1.23456")]
    [TestCase("currency", "NZD")]
    [TestCase("reference", "ABCDEFGHIJKLMNOPQRS")]
    [TestCase("payee.accountNumber", "12  34")]
    [TestCase("payee.name", "")]
    [TestCase("payee", null)]
    public async Task ABodyThatBreaksTheFieldRules_Returns400(string field, string? value)
    {
        var body = BankHttp.Payment();
        object? sent = value switch
        {
            "101" => new string('i', 101),
            "0" => 0m,
            "1.23456" => 1.23456m,
            _ => value
        };
        if (field.StartsWith("payee.", StringComparison.Ordinal))
        {
            ((Dictionary<string, object?>)body["payee"]!)[field["payee.".Length..]] = sent;
        }
        else
        {
            body[field] = sent;
        }

        (await BankHttp.PayAsync(_party, Account, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BankHttp.PaymentsAsync(_control)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task ExecuteMode_AcceptsAndExecutesAtOnce_WithADebitLineInTheFeed()
    {
        var instruction = Guid.NewGuid().ToString();

        var answer = await AnswerAsync(BankHttp.Payment(instruction, 300.00m));

        answer.GetProperty("status").GetString().ShouldBe("Accepted");
        var paymentId = answer.GetProperty("paymentId").GetString()!;
        paymentId.ShouldStartWith(await WorldIdAsync() + "-P");
        var line = (await BankHttp.FeedOkAsync(_party, Account)).Items.ShouldHaveSingleItem();
        line.GetProperty("direction").GetString().ShouldBe("Debit");
        line.GetProperty("amount").GetDecimal().ShouldBe(300.00m);
        line.GetProperty("endToEndId").GetString().ShouldBe(instruction);
        line.GetProperty("returnOf").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("reference").GetString().ShouldBe("K7M3Q9PXB");
        line.GetProperty("payerName").GetString().ShouldBe("J Smith");
        line.GetProperty("payerAccount").GetString().ShouldBe("06-0123-0456789-00");
        line.GetProperty("valueDate").GetString().ShouldBe("2026-11-19");
        var payment = (await BankHttp.PaymentsAsync(_control)).GetProperty("items").EnumerateArray().Single();
        payment.GetProperty("paymentId").GetString().ShouldBe(paymentId);
        payment.GetProperty("status").GetString().ShouldBe("Executed");
        payment.GetProperty("mode").GetString().ShouldBe("Execute");
        payment.GetProperty("instructionId").GetString().ShouldBe(instruction);
        payment.GetProperty("debitLineId").GetString().ShouldBe(line.GetProperty("id").GetString());
    }

    [Test]
    public async Task HoldMode_AcceptsWithoutALine_UntilExecute()
    {
        (await BankHttp.SetModeAsync(_control, "Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await AnswerAsync(BankHttp.Payment());

        answer.GetProperty("status").GetString().ShouldBe("Accepted");
        (await BankHttp.FeedOkAsync(_party, Account)).Items.ShouldBeEmpty();
        var paymentId = answer.GetProperty("paymentId").GetString()!;
        (await StatusAsync(paymentId)).ShouldBe("Held");

        (await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/execute", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await BankHttp.FeedOkAsync(_party, Account)).Items.ShouldHaveSingleItem().GetProperty("direction").GetString().ShouldBe("Debit");
        (await StatusAsync(paymentId)).ShouldBe("Executed");
        var again = await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/execute", "{}");
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await again.ErrorKeysAsync()).ShouldBe(["Status"]);
    }

    [Test]
    public async Task RejectMode_RejectsWithTheReason_AndNeedsOne()
    {
        var missing = await BankHttp.SetModeAsync(_control, "Reject");
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.ErrorKeysAsync()).ShouldBe(["Reason"]);
        (await BankHttp.SetModeAsync(_control, "Reject", "Whatever")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BankHttp.SetModeAsync(_control, "Sometimes")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BankHttp.SetModeAsync(_control, "Reject", "InsufficientFunds")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await AnswerAsync(BankHttp.Payment());

        answer.GetProperty("status").GetString().ShouldBe("Rejected");
        answer.GetProperty("reason").GetString().ShouldBe("InsufficientFunds");
        (await BankHttp.FeedOkAsync(_party, Account)).Items.ShouldBeEmpty();
        (await StatusAsync(answer.GetProperty("paymentId").GetString()!)).ShouldBe("Rejected");
    }

    [Test]
    public async Task TheSameInstructionAndBody_GetsTheFirstAnswer_AndAnotherBodyIs409()
    {
        var instruction = Guid.NewGuid().ToString();
        var first = await AnswerAsync(BankHttp.Payment(instruction));
        (await BankHttp.SetModeAsync(_control, "Reject", "Refused")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await AnswerAsync(BankHttp.Payment(instruction));
        var other = await BankHttp.PayAsync(_party, Account, BankHttp.Payment(instruction, 301m));

        again.GetProperty("paymentId").GetString().ShouldBe(first.GetProperty("paymentId").GetString());
        again.GetProperty("status").GetString().ShouldBe("Accepted");
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BankHttp.PaymentsAsync(_control)).GetProperty("items").GetArrayLength().ShouldBe(1);
        (await BankHttp.FeedOkAsync(_party, Account)).Items.Count.ShouldBe(1);
    }

    [Test]
    public async Task Return_AddsACreditLineThatNamesTheDebitLine_AfterAvailableAfter_AndOnlyOnce()
    {
        var paymentId = (await AnswerAsync(BankHttp.Payment(amount: 300m))).GetProperty("paymentId").GetString()!;
        var debit = (await BankHttp.FeedOkAsync(_party, Account)).Items.Single();

        var response = await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/return",
            """{ "reason": "Account closed", "availableAfter": "P1D" }""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BankHttp.FeedOkAsync(_party, Account)).Items.Count.ShouldBe(1);
        _factory.Clock.Advance(TimeSpan.FromDays(1));
        var line = (await BankHttp.FeedOkAsync(_party, Account)).Items[1];
        line.GetProperty("direction").GetString().ShouldBe("Credit");
        line.GetProperty("amount").GetDecimal().ShouldBe(300m);
        line.GetProperty("returnOf").GetString().ShouldBe(debit.GetProperty("id").GetString());
        line.GetProperty("returnReason").GetString().ShouldBe("Account closed");
        line.GetProperty("payerName").GetString().ShouldBe("J Smith");
        line.GetProperty("endToEndId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await StatusAsync(paymentId)).ShouldBe("Returned");

        var twice = await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/return", """{ "reason": "Again" }""");
        twice.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await twice.ErrorKeysAsync()).ShouldBe(["Status"]);
    }

    [TestCase("Hold", null)]
    [TestCase("Reject", "Refused")]
    public async Task Return_OfAHeldOrRejectedPayment_Returns400(string mode, string? reason)
    {
        (await BankHttp.SetModeAsync(_control, mode, reason)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var paymentId = (await AnswerAsync(BankHttp.Payment())).GetProperty("paymentId").GetString()!;

        var response = await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/return", """{ "reason": "No" }""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe(["Status"]);
    }

    [Test]
    public async Task Return_WithoutAReason_Returns400_AndAnUnknownPayment_404()
    {
        var paymentId = (await AnswerAsync(BankHttp.Payment())).GetProperty("paymentId").GetString()!;

        var missing = await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/return", "{}");
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.ErrorKeysAsync()).ShouldBe(["Reason"]);
        (await _control.PostJsonAsync("/control/bank/payments/nope-P1/return", """{ "reason": "No" }"""))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheBalance_CountsDebitLines()
    {
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = Account, amount = 1000m });
        await AnswerAsync(BankHttp.Payment(amount: 300m));

        var account = (await BankHttp.AccountsAsync(_control)).GetProperty("items").EnumerateArray().Single();
        account.GetProperty("balance").GetDecimal().ShouldBe(700m);
    }

    [Test]
    public async Task AReset_EmptiesThePayments_SetsExecute_AndPaymentIdsNeverRepeat()
    {
        (await BankHttp.SetModeAsync(_control, "Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = (await AnswerAsync(BankHttp.Payment())).GetProperty("paymentId").GetString()!;

        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await BankHttp.OpenAsync(_control, Account);

        (await BankHttp.PaymentsAsync(_control)).GetProperty("items").GetArrayLength().ShouldBe(0);
        var after = await AnswerAsync(BankHttp.Payment());
        after.GetProperty("paymentId").GetString().ShouldNotBe(before);
        (await StatusAsync(after.GetProperty("paymentId").GetString()!)).ShouldBe("Executed");
    }

    [Test]
    public async Task TheThreeControlActions_AreLogged_AndARefusalLogsNothing()
    {
        (await BankHttp.SetModeAsync(_control, "Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var paymentId = (await AnswerAsync(BankHttp.Payment())).GetProperty("paymentId").GetString()!;
        (await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/execute", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/return", """{ "reason": "Closed" }""")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await BankHttp.SetModeAsync(_control, "Reject")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var actions = (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [Action] FROM [ControlLog] ORDER BY [Id]"))
            .Select(row => (string)row[0]).ToList();
        actions.ShouldBe(["BankAccountOpened", "BankPaymentModeChanged", "BankPaymentExecuted", "BankPaymentReturned"]);
    }

    private async Task<JsonElement> AnswerAsync(object body)
    {
        var response = await BankHttp.PayAsync(_party, Account, body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    private async Task<string> StatusAsync(string paymentId) =>
        (await BankHttp.PaymentsAsync(_control)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("paymentId").GetString() == paymentId)
            .GetProperty("status").GetString()!;

    private async Task<string> WorldIdAsync()
    {
        var response = await _control.GetAsync("/control/world");
        using var json = await response.ReadJsonAsync();
        return json.RootElement.GetProperty("worldId").GetGuid().ToString("N");
    }
}
