using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.MarketData;

/// <summary>Corporate actions R48: <c>SimulatorCorporateActionFeed</c> against canned HTTP that follows R44, R45.</summary>
public class SimulatorCorporateActionFeedTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    private static readonly MarketDataConnectionSettings Connection = new(
        "simulator", "Simulator", ".SIM", new Uri("http://simulator:8082/"), Key, TimeoutSeconds: 1, PollMinutes: 1);

    private const string GoodItem = """
        { "id": "w-CA1-V1", "actionId": "w-CA1", "version": 1, "status": "Announced", "symbol": "ACME.SIM", "currency": "USD",
          "kind": "Split", "ratioNew": 2, "ratioHeld": 1, "exDate": "2026-11-24", "recordDate": null, "paymentDate": "2026-12-01" }
        """;

    [Test]
    public async Task AGoodPage_MapsToItems_AndSendsTheKeyToTheFeedRouteAfterTheCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{ "items": [ {{GoodItem}} ], "cursor": "w:1", "hasMore": true }""");

        var result = await Feed(handler).GetAnnouncementsAsync(Connection, "w:0 x", 100, CancellationToken.None);

        result.Failure.ShouldBeNull();
        var page = result.Page.ShouldNotBeNull();
        (page.Cursor, page.HasMore).ShouldBe(("w:1", true));
        page.Items.ShouldBe([
            new CorporateActionFeedItem("w-CA1-V1", "w-CA1", 1, PartyActionStatus.Announced, "ACME.SIM", "USD", CorporateActionKind.Split,
                2, 1, new DateOnly(2026, 11, 24), null, new DateOnly(2026, 12, 1))
        ]);
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.RequestUri!.AbsoluteUri.ShouldBe("http://simulator:8082/party/market/corporate-actions?after=w%3A0%20x&limit=100");
        sent.Headers.GetValues("X-Api-Key").ShouldBe([Key]);
    }

    [Test]
    public async Task TheFirstRead_SendsNoCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "items": [], "cursor": null, "hasMore": false }""");

        (await Feed(handler).GetAnnouncementsAsync(Connection, null, 100, CancellationToken.None)).Page.ShouldNotBeNull().Items.ShouldBeEmpty();

        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe("http://simulator:8082/party/market/corporate-actions?limit=100");
    }

    [TestCase("Consolidation", CorporateActionKind.Consolidation)]
    [TestCase("Bonus", CorporateActionKind.Bonus)]
    [TestCase("Dividend", null)]
    [TestCase("split", null)]
    public async Task TheKind_MapsToAKind_OrToNone(string kind, CorporateActionKind? expected)
    {
        var item = GoodItem.Replace("\"Split\"", $"\"{kind}\"");
        var result = await Feed(new StubHandler(HttpStatusCode.OK, $$"""{ "items": [ {{item}} ], "cursor": "w:1", "hasMore": false }"""))
            .GetAnnouncementsAsync(Connection, null, 100, CancellationToken.None);

        result.Page.ShouldNotBeNull().Items.Single().Kind.ShouldBe(expected);
    }

    [TestCase("\"status\": \"Announced\"", "\"status\": \"Paid\"")]
    [TestCase("\"status\": \"Announced\"", "\"status\": \"1\"")]
    [TestCase("\"exDate\": \"2026-11-24\"", "\"exDate\": \"24/11/2026\"")]
    [TestCase("\"ratioNew\": 2", "\"ratioNew\": 2.5")]
    [TestCase("\"ratioHeld\": 1", "\"ratioHeld\": \"1\"")]
    [TestCase("\"version\": 1", "\"version\": 1.5")]
    public async Task AFieldItCannotRead_ComesThroughEmpty(string from, string to)
    {
        var item = GoodItem.Replace(from, to);
        var result = await Feed(new StubHandler(HttpStatusCode.OK, $$"""{ "items": [ {{item}} ], "cursor": "w:1", "hasMore": false }"""))
            .GetAnnouncementsAsync(Connection, null, 100, CancellationToken.None);

        var read = result.Page.ShouldNotBeNull().Items.Single();
        new object?[] { read.Status, read.ExDate, read.RatioNew, read.RatioHeld, read.Version }.ShouldContain(value => value == null);
        CorporateActionAnnouncementStorable(read).ShouldBeFalse();
    }

    [TestCase(HttpStatusCode.Unauthorized, MarketDataFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, MarketDataFailure.Unavailable)]
    [TestCase(HttpStatusCode.BadRequest, MarketDataFailure.BadResponse)]
    [TestCase(HttpStatusCode.NotFound, MarketDataFailure.BadResponse)]
    public async Task Status_MapsToItsFailure(HttpStatusCode status, MarketDataFailure expected) =>
        (await Feed(new StubHandler(status, "")).GetAnnouncementsAsync(Connection, null, 100, CancellationToken.None))
            .Failure.ShouldBe(expected);

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("""{ "cursor": "w:1", "hasMore": false }""")]
    public async Task ABodyItCannotRead_IsABadResponse(string body) =>
        (await Feed(new StubHandler(HttpStatusCode.OK, body)).GetAnnouncementsAsync(Connection, null, 100, CancellationToken.None))
            .Failure.ShouldBe(MarketDataFailure.BadResponse);

    [Test]
    public async Task ACursorOver500Characters_IsABadResponse()
    {
        var cursor = new string('c', 501);
        (await Feed(new StubHandler(HttpStatusCode.OK, $$"""{ "items": [], "cursor": "{{cursor}}", "hasMore": false }"""))
                .GetAnnouncementsAsync(Connection, null, 100, CancellationToken.None))
            .Failure.ShouldBe(MarketDataFailure.BadResponse);
    }

    [Test]
    public async Task NoAnswerWithinTheTimeout_IsATimeout() =>
        (await Feed(new StubHandler(HttpStatusCode.OK, "{}", TimeSpan.FromSeconds(10)))
                .GetAnnouncementsAsync(Connection, null, 100, CancellationToken.None))
            .Failure.ShouldBe(MarketDataFailure.Timeout);

    [Test]
    public async Task ARefusedConnection_IsUnreachable() =>
        (await new SimulatorCorporateActionFeed(new Clients(new SocketsHttpHandler()))
                .GetAnnouncementsAsync(Connection with { BaseUrl = new Uri("http://127.0.0.1:1") }, null, 100, CancellationToken.None))
            .Failure.ShouldBe(MarketDataFailure.Unreachable);

    [Test]
    public async Task Dispatcher_SendsToTheAdapterOfTheConnection_AndAnUnknownConnectionIsUnreachable()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "items": [], "cursor": "w:0", "hasMore": false }""");
        var dispatcher = new CorporateActionFeedDispatcher(
            new ConfiguredMarketDataConnections([Connection]), [Feed(handler)], NullLogger<CorporateActionFeedDispatcher>.Instance);

        (await dispatcher.GetAnnouncementsAsync("simulator", null, 100, CancellationToken.None)).Page.ShouldNotBeNull();
        (await dispatcher.GetAnnouncementsAsync("other", null, 100, CancellationToken.None)).Failure.ShouldBe(MarketDataFailure.Unreachable);
        handler.Requests.Count.ShouldBe(1);
    }

    private static bool CorporateActionAnnouncementStorable(CorporateActionFeedItem item) =>
        Domain.Entities.CorporateActionAnnouncement.IsStorable(
            item.Id, item.ActionId, item.Version, item.Status, item.Symbol, item.Currency, item.RatioNew, item.RatioHeld, item.ExDate);

    private static SimulatorCorporateActionFeed Feed(HttpMessageHandler handler) => new(new Clients(handler));

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? delay = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
