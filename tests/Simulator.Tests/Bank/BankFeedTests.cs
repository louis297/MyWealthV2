using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>Deposit matching R41, R43–R45: the statement feed on the party surface.</summary>
public class BankFeedTests
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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BankHttp.ResetBank);
        await BankHttp.OpenOkAsync(_control, "SIM-DEMO-USD");
    }

    [Test]
    public async Task Feed_ReturnsTheLinesOldestFirst_WithTheWireShape()
    {
        var first = await BankHttp.AddReceiptOkAsync(_control, new
        {
            accountNumber = "SIM-DEMO-USD",
            amount = 500.00m,
            reference = "K7M 3Q9 PXB",
            payerName = "J SMITH",
            payerAccount = "12-3456-7890042-00",
            valueDate = "2026-11-18"
        });
        var second = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 1.2345m });

        var page = await BankHttp.FeedOkAsync(_party, "sim-demo-usd");

        page.Ids.ShouldBe([first, second]);
        page.HasMore.ShouldBeFalse();
        var item = page.Items[0];
        item.GetProperty("valueDate").GetString().ShouldBe("2026-11-18");
        item.GetProperty("direction").GetString().ShouldBe("Credit");
        item.GetProperty("amount").ValueKind.ShouldBe(JsonValueKind.Number);
        item.GetProperty("amount").GetDecimal().ShouldBe(500.00m);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("payerName").GetString().ShouldBe("J SMITH");
        item.GetProperty("payerAccount").GetString().ShouldBe("12-3456-7890042-00");
        item.GetProperty("reference").GetString().ShouldBe("K7M 3Q9 PXB");
        page.Items[1].GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        page.Items[1].GetProperty("amount").GetDecimal().ShouldBe(1.2345m);
    }

    [Test]
    public async Task Feed_ReadsAfterTheCursor_AndTheCursorIsThePlaceOfTheLastItem()
    {
        var ids = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add(await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = i }));
        }

        var first = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", limit: 2);
        var second = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", first.Cursor, limit: 2);
        var third = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", second.Cursor, limit: 2);
        var empty = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", third.Cursor);

        first.Ids.ShouldBe(ids[..2]);
        first.HasMore.ShouldBeTrue();
        second.Ids.ShouldBe(ids[2..4]);
        third.Ids.ShouldBe(ids[4..]);
        third.HasMore.ShouldBeFalse();
        empty.Ids.ShouldBeEmpty();
        empty.Cursor.ShouldBe(third.Cursor);
    }

    [TestCase(0, 1)]
    [TestCase(-5, 1)]
    [TestCase(500, 100)]
    public async Task Feed_LimitIsClamped(int limit, int expected)
    {
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, $"""
            DECLARE @account int = (SELECT [Id] FROM [BankAccounts] WHERE [AccountNumber] = N'SIM-DEMO-USD');
            DECLARE @i int = 1;
            WHILE @i <= 101
            BEGIN
                INSERT INTO [BankLines] ([OutsideId], [BankAccountId], [Direction], [Amount], [ValueDate], [PostedAt], [AvailableAt], [DeliverTwice])
                VALUES (CONCAT(N'bulk-', @i), @account, 0, 1, '2026-11-19', '2026-11-19T08:00:00+00:00', '2026-11-19T08:00:00+00:00', 0);
                SET @i = @i + 1;
            END;
            """);

        var page = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", limit: limit);

        page.Items.Count.ShouldBe(expected);
        page.HasMore.ShouldBeTrue();
    }

    [Test]
    public async Task Feed_WithNoLines_GivesACursorBeforeTheFirstPlace_ThatReadsLaterLines()
    {
        var empty = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD");
        var id = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 1m });

        empty.Ids.ShouldBeEmpty();
        (await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", empty.Cursor)).Ids.ShouldBe([id]);
    }

    [Test]
    public async Task AvailableAfter_KeepsTheLineOutUntilTheClockReachesIt_ThenPlacesItAfterLinesAlreadyRead()
    {
        var late = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 1m, availableAfter = "PT2H" });
        var now = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 2m });

        var first = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD");
        first.Ids.ShouldBe([now]);

        _factory.Clock.Advance(TimeSpan.FromHours(2));
        var second = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", first.Cursor);

        second.Ids.ShouldBe([late]);
        (await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD")).Ids.ShouldBe([now, late]);
    }

    [Test]
    public async Task AvailableAfter_FollowsTheSimulatorClock()
    {
        var late = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 1m, availableAfter = "P1D" });
        (await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD")).Ids.ShouldBeEmpty();

        (await _control.PostJsonAsync("/control/clock/advance", """{ "by": "P1D" }""")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD")).Ids.ShouldBe([late]);
    }

    [Test]
    public async Task DeliverTwice_TakesTwoPlacesWithOneId()
    {
        var id = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 1m, deliverTwice = true });

        var first = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", limit: 1);
        var second = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", first.Cursor, limit: 1);

        first.Ids.ShouldBe([id]);
        first.HasMore.ShouldBeTrue();
        second.Ids.ShouldBe([id]);
        (await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD")).Ids.ShouldBe([id, id]);
    }

    [Test]
    public async Task ACursorFromAnEarlierWorld_ReadsFromTheStartOfTheCurrentWorld()
    {
        await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 1m });
        var old = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD");
        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await BankHttp.OpenOkAsync(_control, "SIM-DEMO-USD");
        var fresh = await BankHttp.AddReceiptOkAsync(_control, new { accountNumber = "SIM-DEMO-USD", amount = 2m });

        var page = await BankHttp.FeedOkAsync(_party, "SIM-DEMO-USD", old.Cursor);

        page.Ids.ShouldBe([fresh]);
    }

    [TestCase("garbage")]
    [TestCase("00000000000000000000000000000000:abc")]
    [TestCase(":12")]
    public async Task ACursorThatCannotBeRead_Returns400(string cursor)
    {
        (await BankHttp.FeedAsync(_party, "SIM-DEMO-USD", cursor)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownAccount_Returns404()
    {
        (await BankHttp.FeedAsync(_party, "SIM-NONE")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task WithoutTheKey_Returns401_AndDuringAnOutage_503()
    {
        using var anonymous = _factory.PartyClient(apiKey: null);
        (await BankHttp.FeedAsync(anonymous, "SIM-DEMO-USD")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await _control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BankHttp.FeedAsync(_party, "SIM-DEMO-USD")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task TheControlPort_DoesNotServeTheFeed()
    {
        var response = await _control.GetAsync("/party/bank/accounts/SIM-DEMO-USD/statement-lines");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
