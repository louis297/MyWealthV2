using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Broker;

/// <summary>Orders R63: <c>GET /party/broker/reports</c>, the feed the platform's broker feed adapter reads.</summary>
public class BrokerFeedTests
{
    private const int Seed = 42;
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";
    private const string OtherOrderId = "7d1f3a55-0e0b-4c1e-8a57-5b7f0c2d9e42";

    private static readonly string[] Fields =
    [
        "id", "kind", "clientOrderId", "brokerOrderId", "quantity", "price", "tradeDate", "settlementDate", "contractNote", "fillId",
        "settledDate", "reason"
    ];

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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BrokerHttp.ResetBroker);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, $"UPDATE [World] SET [Seed] = {Seed};");
    }

    [Test]
    public async Task AnEmptyFeed_AnswersNoItems_AndACursorBeforeTheFirstPlace()
    {
        var worldId = await MarketHttp.WorldIdAsync();

        var (items, cursor, hasMore) = await BrokerHttp.FeedAsync(_party);

        items.ShouldBeEmpty();
        (cursor, hasMore).ShouldBe(($"{worldId:N}:0", false));
    }

    [Test]
    public async Task TheFeed_ListsEachKindOfReport_OldestFirst_InTheWireShape()
    {
        var worldId = await MarketHttp.WorldIdAsync();
        var prefix = worldId.ToString("N");
        // A fill, a cancellation by the platform, an expiry and a settlement, in the order they happen.
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(quantity: 4m, referencePrice: 100.12m));
        await BrokerHttp.SetModeOkAsync(_control, "Hold");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));
        (await BrokerHttp.RequestCancelOkAsync(_party, OtherOrderId)).ShouldBe("Accepted");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("expires"));
        await MarketHttp.AdvanceAsync(_control, "P4D");

        var (items, cursor, hasMore) = await BrokerHttp.FeedAsync(_party);

        hasMore.ShouldBeFalse();
        items.Count.ShouldBe(4);
        items.ShouldAllBe(item => item.EnumerateObject().Select(property => property.Name).SequenceEqual(Fields));
        items.Select(item => item.GetProperty("id").GetString()).ShouldBe([$"{prefix}-X1", $"{prefix}-X2", $"{prefix}-X3", $"{prefix}-X4"]);
        items.Select(item => item.GetProperty("kind").GetString()).ShouldBe(["Fill", "Cancelled", "Expired", "Settled"]);
        items.Select(item => item.GetProperty("clientOrderId").GetString()).ShouldBe([OrderId, OtherOrderId, "expires", OrderId]);
        items.Select(item => item.GetProperty("brokerOrderId").GetString()).ShouldBe([$"{prefix}-O1", $"{prefix}-O2", $"{prefix}-O3", $"{prefix}-O1"]);

        var fill = items[0];
        fill.GetProperty("quantity").ValueKind.ShouldBe(JsonValueKind.Number);
        fill.GetProperty("price").ValueKind.ShouldBe(JsonValueKind.Number);
        (fill.GetProperty("quantity").GetDecimal(), fill.GetProperty("price").GetDecimal(), fill.GetProperty("tradeDate").GetString(),
                fill.GetProperty("settlementDate").GetString(), fill.GetProperty("contractNote").GetString())
            .ShouldBe((4m, 100.12m, "2026-11-19", "2026-11-23", "CN-1"));
        Nulls(fill, "fillId", "settledDate", "reason");

        var cancelled = items[1];
        cancelled.GetProperty("reason").GetString().ShouldBe("Requested");
        Nulls(cancelled, "quantity", "price", "tradeDate", "settlementDate", "contractNote", "fillId", "settledDate");

        Nulls(items[2], "quantity", "price", "tradeDate", "settlementDate", "contractNote", "fillId", "settledDate", "reason");

        var settled = items[3];
        (settled.GetProperty("fillId").GetString(), settled.GetProperty("settledDate").GetString()).ShouldBe(($"{prefix}-X1", "2026-11-23"));
        Nulls(settled, "quantity", "price", "tradeDate", "settlementDate", "contractNote", "reason");

        // The cursor is the place of the last report: the next read answers nothing new.
        var (next, nextCursor, _) = await BrokerHttp.FeedAsync(_party, $"?after={Uri.EscapeDataString(cursor)}");
        next.ShouldBeEmpty();
        nextCursor.ShouldBe(cursor);
    }

    [Test]
    public async Task TheFeed_IsReadPageByPage_AfterTheCursor()
    {
        var worldId = await MarketHttp.WorldIdAsync();
        var prefix = worldId.ToString("N");
        foreach (var n in Enumerable.Range(1, 5))
        {
            await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order($"order-{n}"));
        }

        var first = await BrokerHttp.FeedAsync(_party, "?limit=2");
        var second = await BrokerHttp.FeedAsync(_party, $"?after={Uri.EscapeDataString(first.Cursor)}&limit=2");
        var third = await BrokerHttp.FeedAsync(_party, $"?after={Uri.EscapeDataString(second.Cursor)}&limit=2");

        Ids(first.Items).ShouldBe([$"{prefix}-X1", $"{prefix}-X2"]);
        Ids(second.Items).ShouldBe([$"{prefix}-X3", $"{prefix}-X4"]);
        Ids(third.Items).ShouldBe([$"{prefix}-X5"]);
        (first.HasMore, second.HasMore, third.HasMore).ShouldBe((true, true, false));
        first.Cursor.ShouldStartWith($"{prefix}:");
        new[] { first.Cursor, second.Cursor, third.Cursor }.Distinct().Count().ShouldBe(3);

        // A report made after a read is after its cursor.
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("order-6"));
        Ids((await BrokerHttp.FeedAsync(_party, $"?after={Uri.EscapeDataString(third.Cursor)}")).Items).ShouldBe([$"{prefix}-X6"]);
    }

    [Test]
    public async Task TheLimit_IsHeldBetween1And100_AndIs100WhenMissing()
    {
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, "UPDATE [BrokerSettings] SET [Mode] = 1;");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        // 101 reports of one working order, written straight to the broker's books.
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, """
            DECLARE @order int = (SELECT [Id] FROM [BrokerOrders]);
            DECLARE @n int = 0;
            WHILE @n < 101
            BEGIN
                INSERT INTO [BrokerReports] ([Kind], [OrderId], [Reason]) VALUES (1, @order, N'Requested');
                SET @n += 1;
            END
            """);

        (await BrokerHttp.FeedAsync(_party)).Items.Count.ShouldBe(100);
        (await BrokerHttp.FeedAsync(_party)).HasMore.ShouldBeTrue();
        (await BrokerHttp.FeedAsync(_party, "?limit=1000")).Items.Count.ShouldBe(100);
        (await BrokerHttp.FeedAsync(_party, "?limit=0")).Items.Count.ShouldBe(1);
        (await BrokerHttp.FeedAsync(_party, "?limit=-5")).Items.Count.ShouldBe(1);
        (await BrokerHttp.FeedAsync(_party, "?limit=101")).HasMore.ShouldBeTrue();
    }

    [Test]
    public async Task ACursorOfAnotherWorld_ReadsThisWorldFromItsStart_AndOneThatCannotBeRead_Returns400()
    {
        var worldId = await MarketHttp.WorldIdAsync();
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));

        var foreign = await BrokerHttp.FeedAsync(_party, $"?after={Guid.NewGuid():N}:1");

        Ids(foreign.Items).ShouldBe([$"{worldId:N}-X1", $"{worldId:N}-X2"]);
        foreach (var cursor in new[] { "7", "x:1", $"{worldId:N}", $"{worldId:N}:-1", $"{worldId:N}:one", $"{worldId:D}:1" })
        {
            (await _party.GetAsync($"/party/broker/reports?after={Uri.EscapeDataString(cursor)}")).StatusCode
                .ShouldBe(HttpStatusCode.BadRequest, cursor);
        }
    }

    [Test]
    public async Task TheFeed_BringsTheBooksUpToDate_BeforeItAnswers()
    {
        await BrokerHttp.SetModeOkAsync(_control, "Hold");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        (await BrokerHttp.FeedAsync(_party)).Items.ShouldBeEmpty();

        _factory.Clock.Advance(TimeSpan.FromDays(1));

        var (items, _, _) = await BrokerHttp.FeedAsync(_party);
        items.ShouldHaveSingleItem().GetProperty("kind").GetString().ShouldBe("Expired");
    }

    [Test]
    public async Task AfterAReset_TheReportsOfTheNewWorldCountFrom1_UnderItsId()
    {
        var oldWorld = await MarketHttp.WorldIdAsync();
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        var old = await BrokerHttp.FeedAsync(_party);
        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var newWorld = await MarketHttp.WorldIdAsync();

        (await BrokerHttp.FeedAsync(_party, $"?after={Uri.EscapeDataString(old.Cursor)}")).Items.ShouldBeEmpty();
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        var (items, cursor, _) = await BrokerHttp.FeedAsync(_party, $"?after={Uri.EscapeDataString(old.Cursor)}");

        Ids(old.Items).ShouldBe([$"{oldWorld:N}-X1"]);
        Ids(items).ShouldBe([$"{newWorld:N}-X1"]);
        items.Single().GetProperty("brokerOrderId").GetString().ShouldBe($"{newWorld:N}-O1");
        cursor.ShouldStartWith($"{newWorld:N}:");
    }

    [Test]
    public async Task WithoutTheKey_Returns401_DuringAnOutage503_AndTheControlPortDoesNotServeIt()
    {
        using var noKey = _factory.PartyClient(apiKey: null);
        using var wrongKey = _factory.PartyClient("wrong-party-api-key-0123456789abcdef");

        (await noKey.GetAsync("/party/broker/reports")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrongKey.GetAsync("/party/broker/reports")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _control.GetAsync("/party/broker/reports")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _party.GetAsync("/party/broker/reports")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    private static List<string?> Ids(IEnumerable<JsonElement> items) =>
        items.Select(item => item.GetProperty("id").GetString()).ToList();

    private static void Nulls(JsonElement item, params string[] fields)
    {
        foreach (var field in fields)
        {
            item.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }
    }
}
