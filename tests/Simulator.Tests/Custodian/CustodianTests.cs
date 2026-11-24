using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Custodian;

/// <summary>In-kind transfers R60–R66, R69–R71: the simulator custodian's accounts, transfers, movements and feed.</summary>
public class CustodianTests
{
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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await CustodianHttp.OpenOkAsync(_control);
    }

    [Test]
    public async Task Script0012_IsApplied() =>
        (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0012_custodian.sql';")).Single()[0].ShouldBe(1);

    [Test]
    public async Task Execute_AcceptsTheTransfer_AndAddsATransferMovementToTheFeed()
    {
        var prefix = (await MarketHttp.WorldIdAsync()).ToString("N");

        var answer = await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer());

        answer.GetProperty("status").GetString().ShouldBe("Accepted");
        answer.GetProperty("transferId").GetString().ShouldBe($"{prefix}-T1");
        var (items, cursor, hasMore, positions) = await CustodianHttp.FeedAsync(_party);
        var item = items.ShouldHaveSingleItem();
        item.GetProperty("id").GetString().ShouldBe($"{prefix}-M1");
        (item.GetProperty("direction").GetString(), item.GetProperty("kind").GetString()).ShouldBe(("In", "Transfer"));
        (item.GetProperty("symbol").GetString(), item.GetProperty("currency").GetString()).ShouldBe(("VTI", "USD"));
        item.GetProperty("quantity").ValueKind.ShouldBe(JsonValueKind.Number);
        item.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        item.GetProperty("settlementDate").GetString().ShouldBe("2026-11-19");
        item.GetProperty("instructionId").GetString().ShouldBe("6b1f0c2e-0000-4000-8000-000000000001");
        item.GetProperty("counterpartyName").GetString().ShouldBe("Harbour Brokers");
        item.GetProperty("counterpartyAccount").GetString().ShouldBe("HB-0044172");
        item.GetProperty("tradeReference").ValueKind.ShouldBe(JsonValueKind.Null);
        cursor.ShouldBe($"{prefix}:1");
        hasMore.ShouldBeFalse();
        positions.Select(position => (position.GetProperty("symbol").GetString(), position.GetProperty("quantity").GetDecimal()))
            .ShouldBe([("VTI", 100m)]);
    }

    [Test]
    public async Task TheSameInstructionAndBody_GetsTheFirstAnswer_AndAnotherBodyIs409()
    {
        var first = await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer());
        var again = await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer());
        var other = await CustodianHttp.TransferAsync(_party, CustodianHttp.Transfer(quantity: 99m));

        again.GetProperty("transferId").GetString().ShouldBe(first.GetProperty("transferId").GetString());
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CustodianHttp.FeedAsync(_party)).Items.Count.ShouldBe(1);
    }

    [TestCase("currency")]
    [TestCase("direction")]
    [TestCase("quantity")]
    [TestCase("counterparty")]
    public async Task ABodyOutsideTheRules_Is400(string broken)
    {
        var body = broken switch
        {
            "currency" => CustodianHttp.Transfer(currency: "NZD"),
            "direction" => CustodianHttp.Transfer(direction: "Sideways"),
            "quantity" => CustodianHttp.Transfer(quantity: 0.000000001m),
            _ => CustodianHttp.Transfer(counterpartyName: "")
        };

        (await CustodianHttp.TransferAsync(_party, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnUnknownAccount_Is404_OnBothPartyRoutes()
    {
        (await CustodianHttp.TransferAsync(_party, CustodianHttp.Transfer(), "NO-SUCH")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _party.GetAsync("/party/custodian/accounts/NO-SUCH/movements")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Hold_AcceptsAndHolds_ThenExecute_WithATypedQuantity_AddsTheMovement()
    {
        (await CustodianHttp.SetTransferModeAsync(_control, "Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var answer = await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer());
        answer.GetProperty("status").GetString().ShouldBe("Accepted");
        (await CustodianHttp.FeedAsync(_party)).Items.ShouldBeEmpty();
        var transferId = answer.GetProperty("transferId").GetString()!;

        (await CustodianHttp.ExecuteAsync(_control, transferId, 90m)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await CustodianHttp.FeedAsync(_party)).Items.ShouldHaveSingleItem().GetProperty("quantity").GetDecimal().ShouldBe(90m);
        (await CustodianHttp.ExecuteAsync(_control, transferId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var transfers = await CustodianHttp.GetAsync(_control, "/control/custodian/transfers");
        var row = transfers.GetProperty("items").EnumerateArray().Single();
        (row.GetProperty("status").GetString(), row.GetProperty("modeAtReceipt").GetString()).ShouldBe(("Executed", "Hold"));
    }

    [Test]
    public async Task Hold_ThenExecute_WithTheInstructedQuantity()
    {
        (await CustodianHttp.SetTransferModeAsync(_control, "Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var transferId = (await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer())).GetProperty("transferId").GetString()!;

        (await CustodianHttp.ExecuteAsync(_control, transferId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await CustodianHttp.FeedAsync(_party)).Items.Single().GetProperty("quantity").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task Reject_RejectsWithTheReason_AndNeedsOne()
    {
        (await CustodianHttp.SetTransferModeAsync(_control, "Reject")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustodianHttp.SetTransferModeAsync(_control, "Reject", "InsufficientPosition")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer(direction: "Out"));

        (answer.GetProperty("status").GetString(), answer.GetProperty("reason").GetString()).ShouldBe(("Rejected", "InsufficientPosition"));
        (await CustodianHttp.FeedAsync(_party)).Items.ShouldBeEmpty();
    }

    [Test]
    public async Task ASimulatedInstrumentInAnotherCurrency_IsRejectedUnknownInstrument_InAnyMode()
    {
        await MarketHttp.AddOkAsync(_control, "KIWI.SIM", "NZD", 10m);

        var answer = await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer(symbol: "KIWI.SIM"));

        (answer.GetProperty("status").GetString(), answer.GetProperty("reason").GetString()).ShouldBe(("Rejected", "UnknownInstrument"));
    }

    [Test]
    public async Task AControlMovement_IsInTheFeed_AndOneLeftOut_IsPassedOver_ButCountsInThePositions()
    {
        (await CustodianHttp.AddMovementAsync(_control, new { accountNumber = CustodianHttp.Account, direction = "In", symbol = "VTI", quantity = 800m,
            counterpartyName = "Acme Wealth", reference = "Bulk delivery" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CustodianHttp.AddMovementAsync(_control, new { accountNumber = CustodianHttp.Account, direction = "Out", symbol = "VTI", quantity = 5m,
            omitFromFeed = true })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var (items, cursor, _, positions) = await CustodianHttp.FeedAsync(_party);

        var item = items.ShouldHaveSingleItem();
        (item.GetProperty("kind").GetString(), item.GetProperty("instructionId").ValueKind).ShouldBe(("Transfer", JsonValueKind.Null));
        item.GetProperty("reference").GetString().ShouldBe("Bulk delivery");
        cursor.ShouldEndWith(":2");
        positions.Single().GetProperty("quantity").GetDecimal().ShouldBe(795m);
        var movements = await CustodianHttp.GetAsync(_control, $"/control/custodian/accounts/{CustodianHttp.Account}/movements");
        movements.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("omitFromFeed").GetBoolean()).ShouldBe([true, false]);
        (await CustodianHttp.PositionsAsync(_control))["VTI"].ShouldBe(795m);
    }

    [Test]
    public async Task AControlMovement_OnAnAccountThatIsNotOpen_Is400()
    {
        var response = await CustodianHttp.AddMovementAsync(_control, new { accountNumber = "NO-SUCH", direction = "In", symbol = "VTI", quantity = 1m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TheFeed_PagesAfterTheCursor_AndPositionsAreAtTheCursor()
    {
        for (var i = 1; i <= 3; i++)
        {
            await CustodianHttp.TransferOkAsync(_party, CustodianHttp.Transfer(instructionId: $"instruction-{i}", quantity: i));
        }

        var first = await CustodianHttp.FeedAsync(_party, limit: 2);
        var second = await CustodianHttp.FeedAsync(_party, after: first.Cursor, limit: 2);

        (first.Items.Count, first.HasMore).ShouldBe((2, true));
        first.Positions.Single().GetProperty("quantity").GetDecimal().ShouldBe(3m);
        (second.Items.Count, second.HasMore).ShouldBe((1, false));
        second.Positions.Single().GetProperty("quantity").GetDecimal().ShouldBe(6m);
        (await _party.GetAsync($"/party/custodian/accounts/{CustodianHttp.Account}/movements?after=garbage")).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task OpeningAnAccountTwice_Is400_AndSettingsShowTheModes()
    {
        (await CustodianHttp.OpenAsync(_control, CustodianHttp.Account)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustodianHttp.SetRoundingModeAsync(_control, "WholeShares")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var settings = await CustodianHttp.GetAsync(_control, "/control/custodian");

        (settings.GetProperty("transferMode").GetString(), settings.GetProperty("roundingMode").GetString()).ShouldBe(("Execute", "WholeShares"));
    }
}
