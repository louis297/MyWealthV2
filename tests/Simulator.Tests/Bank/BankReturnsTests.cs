using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>Bank-returns R12–R15: the simulator bank returns a receipt the platform sends back, and recalls a receipt.</summary>
public class BankReturnsTests
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
    public async Task Script0005_IsApplied()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0005_bank_returns.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task AReturnOfAReceipt_IsExecuted_AsADebitLineThatNamesItAndPaysTheOriginalPayer()
    {
        var receipt = await ReceiptAsync(120.50m);
        var instruction = Guid.NewGuid().ToString();

        var answer = await AnswerAsync(Return(instruction, receipt, 120.50m));

        answer.GetProperty("status").GetString().ShouldBe("Accepted");
        var debit = (await BankHttp.FeedOkAsync(_party, Account)).Items.Single(item => item.GetProperty("id").GetString() != receipt);
        debit.GetProperty("direction").GetString().ShouldBe("Debit");
        debit.GetProperty("amount").GetDecimal().ShouldBe(120.50m);
        debit.GetProperty("endToEndId").GetString().ShouldBe(instruction);
        debit.GetProperty("returnOf").GetString().ShouldBe(receipt);
        debit.GetProperty("payerName").GetString().ShouldBe("A Stranger");
        debit.GetProperty("payerAccount").GetString().ShouldBe("02-0100-0123456-00");
        var payment = (await BankHttp.PaymentsAsync(_control)).GetProperty("items").EnumerateArray().Single();
        payment.GetProperty("status").GetString().ShouldBe("Executed");
        payment.GetProperty("returnOfLineId").GetString().ShouldBe(receipt);
        payment.GetProperty("debitLineId").GetString().ShouldBe(debit.GetProperty("id").GetString());
        payment.GetProperty("payeeName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task AHeldReturn_IsExecutedLater_TheSameWay()
    {
        var receipt = await ReceiptAsync(40m);
        (await BankHttp.SetModeAsync(_control, "Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var answer = await AnswerAsync(Return(Guid.NewGuid().ToString(), receipt, 40m));
        var paymentId = answer.GetProperty("paymentId").GetString()!;

        var execute = await _control.PostJsonAsync($"/control/bank/payments/{paymentId}/execute", "{}");

        execute.StatusCode.ShouldBe(HttpStatusCode.OK, await execute.Content.ReadAsStringAsync());
        var debit = (await BankHttp.FeedOkAsync(_party, Account)).Items.Single(item => item.GetProperty("direction").GetString() == "Debit");
        debit.GetProperty("returnOf").GetString().ShouldBe(receipt);
        debit.GetProperty("payerName").GetString().ShouldBe("A Stranger");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task BothOrNeitherOfPayeeAndReturnOf_Returns400(bool both)
    {
        var receipt = await ReceiptAsync(40m);
        var body = BankHttp.Payment();
        if (both)
        {
            body["returnOf"] = receipt;
        }
        else
        {
            body.Remove("payee");
        }

        (await BankHttp.PayAsync(_party, Account, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BankHttp.PaymentsAsync(_control)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task AReturnOfALineTheAccountDoesNotHave_IsRejectedAsUnknownLine()
    {
        var answer = await AnswerAsync(Return(Guid.NewGuid().ToString(), "no-such-line", 40m));

        answer.GetProperty("status").GetString().ShouldBe("Rejected");
        answer.GetProperty("reason").GetString().ShouldBe("UnknownLine");
        (await BankHttp.FeedOkAsync(_party, Account)).Items.ShouldBeEmpty();
    }

    [Test]
    public async Task AReturnOfAReturnLine_OrOfALineReturnedOrRecalledBefore_IsRejectedAsUnknownLine()
    {
        var returned = await ReceiptAsync(40m);
        (await AnswerAsync(Return(Guid.NewGuid().ToString(), returned, 40m))).GetProperty("status").GetString().ShouldBe("Accepted");
        var recalled = await ReceiptAsync(50m);
        (await RecallAsync(recalled, "Dishonoured")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var paid = (await AnswerAsync(BankHttp.Payment(amount: 10m))).GetProperty("paymentId").GetString()!;
        var paymentReturn = await _control.PostJsonAsync($"/control/bank/payments/{paid}/return", """{ "reason": "Account closed" }""");
        paymentReturn.StatusCode.ShouldBe(HttpStatusCode.OK, await paymentReturn.Content.ReadAsStringAsync());
        using var returnJson = JsonDocument.Parse(await paymentReturn.Content.ReadAsStringAsync());
        var returnLine = returnJson.RootElement.GetProperty("returnLineId").GetString()!;

        foreach (var (line, amount) in new[] { (returned, 40m), (recalled, 50m), (returnLine, 10m) })
        {
            var answer = await AnswerAsync(Return(Guid.NewGuid().ToString(), line, amount));
            answer.GetProperty("status").GetString().ShouldBe("Rejected", line);
            answer.GetProperty("reason").GetString().ShouldBe("UnknownLine", line);
        }
    }

    [Test]
    public async Task AReturnAgain_WithTheSameInstructionAndBody_GetsTheFirstAnswer()
    {
        var receipt = await ReceiptAsync(40m);
        var body = Return(Guid.NewGuid().ToString(), receipt, 40m);
        var first = await AnswerAsync(body);

        var second = await AnswerAsync(body);

        second.GetProperty("paymentId").GetString().ShouldBe(first.GetProperty("paymentId").GetString());
        (await BankHttp.PaymentsAsync(_control)).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Recall_AddsADebitLineThatNamesTheReceipt_AfterAvailableAfter()
    {
        var receipt = await ReceiptAsync(75m);

        var response = await RecallAsync(receipt, "Payer's bank recalled the payment", "PT1H");

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var recallId = json.RootElement.GetProperty("id").GetString()!;
        (await BankHttp.FeedOkAsync(_party, Account)).Ids.ShouldBe([receipt]);
        _factory.Clock.Advance(TimeSpan.FromHours(1));
        var recall = (await BankHttp.FeedOkAsync(_party, Account)).Items.Single(item => item.GetProperty("id").GetString() == recallId);
        recall.GetProperty("direction").GetString().ShouldBe("Debit");
        recall.GetProperty("amount").GetDecimal().ShouldBe(75m);
        recall.GetProperty("returnOf").GetString().ShouldBe(receipt);
        recall.GetProperty("returnReason").GetString().ShouldBe("Payer's bank recalled the payment");
        recall.GetProperty("endToEndId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Recall_Twice_OrOfAReturnLine_OrOfADebit_Returns400AtStatus()
    {
        var receipt = await ReceiptAsync(75m);
        (await RecallAsync(receipt, "Dishonoured")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var paid = (await AnswerAsync(BankHttp.Payment(amount: 10m))).GetProperty("paymentId").GetString()!;
        var paymentReturn = await _control.PostJsonAsync($"/control/bank/payments/{paid}/return", """{ "reason": "Account closed" }""");
        using var returnJson = JsonDocument.Parse(await paymentReturn.Content.ReadAsStringAsync());
        var returnLine = returnJson.RootElement.GetProperty("returnLineId").GetString()!;
        var debitLine = returnJson.RootElement.GetProperty("debitLineId").GetString()!;

        foreach (var line in new[] { receipt, returnLine, debitLine })
        {
            var response = await RecallAsync(line, "Again");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, line);
            (await response.ErrorKeysAsync()).ShouldContain("Status");
        }
    }

    [Test]
    public async Task Recall_OfALineAlreadySentBack_Returns400AtStatus()
    {
        var receipt = await ReceiptAsync(40m);
        (await AnswerAsync(Return(Guid.NewGuid().ToString(), receipt, 40m))).GetProperty("status").GetString().ShouldBe("Accepted");

        var response = await RecallAsync(receipt, "Dishonoured");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Recall_OfAnUnknownLine_Returns404_AndABadReason400()
    {
        (await RecallAsync("no-such-line", "Dishonoured")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var receipt = await ReceiptAsync(40m);
        var response = await RecallAsync(receipt, "");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldContain("Reason");
    }

    [Test]
    public async Task ARecall_IsLogged_AndA400WritesNoLogRow()
    {
        var receipt = await ReceiptAsync(40m);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, "DELETE FROM [ControlLog];");

        (await RecallAsync(receipt, "Dishonoured")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RecallAsync(receipt, "Again")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [Action] FROM [ControlLog];");
        rows.Select(row => (string)row[0]).ShouldBe(["BankReceiptRecalled"]);
    }

    private async Task<string> ReceiptAsync(decimal amount) =>
        await BankHttp.AddReceiptOkAsync(_control, new
        {
            accountNumber = Account,
            amount,
            reference = "Wrong firm",
            payerName = "A Stranger",
            payerAccount = "02-0100-0123456-00"
        });

    private Task<HttpResponseMessage> RecallAsync(string outsideId, string reason, string? availableAfter = null) =>
        _control.PostJsonAsync(
            $"/control/bank/lines/{Uri.EscapeDataString(outsideId)}/recall",
            JsonSerializer.Serialize(new { reason, availableAfter }));

    private static Dictionary<string, object?> Return(string instructionId, string returnOf, decimal amount) => new()
    {
        ["instructionId"] = instructionId,
        ["amount"] = amount,
        ["currency"] = "USD",
        ["returnOf"] = returnOf
    };

    private async Task<JsonElement> AnswerAsync(object body)
    {
        var response = await BankHttp.PayAsync(_party, Account, body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
