using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Broker;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Broker;

/// <summary>Orders R67: <c>SimulatorBrokerFeed</c> against canned HTTP that follows R63.</summary>
public class SimulatorBrokerFeedTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";

    private static readonly BrokerConnectionSettings Connection = new(
        "simulator", "Simulator", new Uri("http://simulator:8082/"), Key, TimeoutSeconds: 1, PollMinutes: 1);

    private const string Fill = """
        { "id": "w-X1", "kind": "Fill", "clientOrderId": "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11", "brokerOrderId": "w-O1", "quantity": 4,
          "price": 100.12, "tradeDate": "2026-11-23", "settlementDate": "2026-11-25", "contractNote": "CN-1", "fillId": null,
          "settledDate": null, "reason": null }
        """;

    private const string Cancelled = """
        { "id": "w-X2", "kind": "Cancelled", "clientOrderId": "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11", "brokerOrderId": "w-O1", "quantity": null,
          "price": null, "tradeDate": null, "settlementDate": null, "contractNote": null, "fillId": null, "settledDate": null,
          "reason": "Requested" }
        """;

    private const string Settled = """
        { "id": "w-X3", "kind": "Settled", "clientOrderId": "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11", "brokerOrderId": "w-O1", "quantity": null,
          "price": null, "tradeDate": null, "settlementDate": null, "contractNote": null, "fillId": "w-X1", "settledDate": "2026-11-25",
          "reason": null }
        """;

    private static string Page(params string[] items) => $$"""{ "items": [ {{string.Join(", ", items)}} ], "cursor": "w:3", "hasMore": true }""";

    [Test]
    public async Task AGoodPageOfEachKind_MapsToItems_AndSendsTheKeyToTheFeedRouteAfterTheCursor()
    {
        var expired = Cancelled.Replace("\"Cancelled\"", "\"Expired\"").Replace("w-X2", "w-X4").Replace("\"Requested\"", "null");
        var handler = new StubHandler(HttpStatusCode.OK, Page(Fill, Cancelled, Settled, expired));

        var result = await Feed(handler).GetReportsAsync(Connection, "w:0 x", 100, CancellationToken.None);

        result.Failure.ShouldBeNull();
        var page = result.Page.ShouldNotBeNull();
        (page.Cursor, page.HasMore).ShouldBe(("w:3", true));
        page.Items.ShouldBe([
            new BrokerFeedItem("w-X1", ExecutionReportKind.Fill, OrderId, "w-O1", 4m, 100.12m, new DateOnly(2026, 11, 23),
                new DateOnly(2026, 11, 25), "CN-1", null, null, null),
            new BrokerFeedItem("w-X2", ExecutionReportKind.Cancelled, OrderId, "w-O1", null, null, null, null, null, null, null, "Requested"),
            new BrokerFeedItem("w-X3", ExecutionReportKind.Settled, OrderId, "w-O1", null, null, null, null, null, "w-X1",
                new DateOnly(2026, 11, 25), null),
            new BrokerFeedItem("w-X4", ExecutionReportKind.Expired, OrderId, "w-O1", null, null, null, null, null, null, null, null)
        ]);
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Get);
        sent.RequestUri!.AbsoluteUri.ShouldBe("http://simulator:8082/party/broker/reports?after=w%3A0%20x&limit=100");
        sent.Headers.GetValues("X-Api-Key").ShouldBe([Key]);
    }

    [Test]
    public async Task TheFirstRead_SendsNoCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "items": [], "cursor": null, "hasMore": false }""");

        var page = (await Feed(handler).GetReportsAsync(Connection, null, 100, CancellationToken.None)).Page.ShouldNotBeNull();

        (page.Items.Count, page.Cursor, page.HasMore).ShouldBe((0, (string?)null, false));
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe("http://simulator:8082/party/broker/reports?limit=100");
    }

    [TestCase("\"Breaks\"")]
    [TestCase("\"fill\"")]
    [TestCase("null")]
    [TestCase("7")]
    public async Task AKindTheAdapterDoesNotKnow_ComesThroughAsNoKind(string kind)
    {
        var item = Fill.Replace("\"kind\": \"Fill\"", $"\"kind\": {kind}");

        var page = (await Feed(new StubHandler(HttpStatusCode.OK, Page(item))).GetReportsAsync(Connection, null, 100, CancellationToken.None))
            .Page.ShouldNotBeNull();

        var read = page.Items.ShouldHaveSingleItem();
        (read.Id, read.Kind, read.ClientOrderId).ShouldBe(("w-X1", (ExecutionReportKind?)null, OrderId));
        ExecutionReport.IsStorable(read.Id, read.Kind, read.ClientOrderId, read.Quantity, read.Price, read.TradeDate, read.SettlementDate,
            read.FillId, read.SettledDate).ShouldBeTrue();
    }

    [TestCase("\"quantity\": 4", "\"quantity\": \"4\"")]
    [TestCase("\"price\": 100.12", "\"price\": \"100.12\"")]
    [TestCase("\"price\": 100.12", "\"price\": null")]
    [TestCase("\"tradeDate\": \"2026-11-23\"", "\"tradeDate\": \"23/11/2026\"")]
    [TestCase("\"settlementDate\": \"2026-11-25\"", "\"settlementDate\": \"2026-11-25T00:00:00Z\"")]
    [TestCase("\"clientOrderId\": \"2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11\"", "\"clientOrderId\": 17")]
    public async Task AFieldItCannotRead_ComesThroughEmpty_SoThePullRefusesThePage(string field, string broken)
    {
        var item = Fill.Replace(field, broken);

        var page = (await Feed(new StubHandler(HttpStatusCode.OK, Page(item))).GetReportsAsync(Connection, null, 100, CancellationToken.None))
            .Page.ShouldNotBeNull();

        var read = page.Items.ShouldHaveSingleItem();
        read.Kind.ShouldBe(ExecutionReportKind.Fill);
        ExecutionReport.IsStorable(read.Id, read.Kind, read.ClientOrderId, read.Quantity, read.Price, read.TradeDate, read.SettlementDate,
            read.FillId, read.SettledDate).ShouldBeFalse();
    }

    [Test]
    public async Task ASettledDateItCannotRead_ComesThroughEmpty()
    {
        var item = Settled.Replace("\"settledDate\": \"2026-11-25\"", "\"settledDate\": \"soon\"");

        var read = (await Feed(new StubHandler(HttpStatusCode.OK, Page(item))).GetReportsAsync(Connection, null, 100, CancellationToken.None))
            .Page.ShouldNotBeNull().Items.ShouldHaveSingleItem();

        (read.Kind, read.FillId, read.SettledDate).ShouldBe((ExecutionReportKind.Settled, "w-X1", (DateOnly?)null));
    }

    private const string Settlement = """
        { "id": "w-X9", "kind": "Settlement", "clientOrderId": null, "brokerOrderId": null, "quantity": null, "price": null,
          "tradeDate": null, "settlementDate": null, "contractNote": null, "fillId": null, "settledDate": "2026-11-25", "reason": null,
          "settlementId": "0f8fad5b-S3", "settlementAccount": "SIM-DEMO-USD", "currency": "USD", "netAmount": -1001.5, "fillCount": 3 }
        """;

    [Test]
    public async Task ASettlementItem_AndASettledItemWithItsSettlementId_MapWithTheSettlementFields()
    {
        var settled = Settled.Replace("\"reason\": null", "\"reason\": null, \"settlementId\": \"0f8fad5b-S3\", \"settlementAccount\": null, \"currency\": null, \"netAmount\": null, \"fillCount\": null");

        var page = (await Feed(new StubHandler(HttpStatusCode.OK, Page(settled, Settlement))).GetReportsAsync(Connection, null, 100, CancellationToken.None))
            .Page.ShouldNotBeNull();

        page.Items.ShouldBe([
            new BrokerFeedItem("w-X3", ExecutionReportKind.Settled, OrderId, "w-O1", null, null, null, null, null, "w-X1",
                new DateOnly(2026, 11, 25), null, new MyWealthV2.Domain.Orders.ExecutionReportSettlement("0f8fad5b-S3")),
            new BrokerFeedItem("w-X9", ExecutionReportKind.Settlement, null, null, null, null, null, null, null, null,
                new DateOnly(2026, 11, 25), null,
                new MyWealthV2.Domain.Orders.ExecutionReportSettlement("0f8fad5b-S3", "SIM-DEMO-USD", "USD", -1001.5m, 3))
        ]);
        page.Items.All(item => ExecutionReport.IsStorable(item.Id, item.Kind, item.ClientOrderId, item.Quantity, item.Price,
            item.TradeDate, item.SettlementDate, item.FillId, item.SettledDate, item.Settlement)).ShouldBeTrue();
    }

    [TestCase("\"netAmount\": -1001.5", "\"netAmount\": \"-1001.5\"")]
    [TestCase("\"fillCount\": 3", "\"fillCount\": 3.5")]
    [TestCase("\"fillCount\": 3", "\"fillCount\": \"3\"")]
    public async Task ASettlementFieldItCannotRead_ComesThroughEmpty_SoThePullRefusesThePage(string field, string broken)
    {
        var item = Settlement.Replace(field, broken);

        var read = (await Feed(new StubHandler(HttpStatusCode.OK, Page(item))).GetReportsAsync(Connection, null, 100, CancellationToken.None))
            .Page.ShouldNotBeNull().Items.ShouldHaveSingleItem();

        read.Kind.ShouldBe(ExecutionReportKind.Settlement);
        ExecutionReport.IsStorable(read.Id, read.Kind, read.ClientOrderId, read.Quantity, read.Price, read.TradeDate, read.SettlementDate,
            read.FillId, read.SettledDate, read.Settlement).ShouldBeFalse();
    }

    [TestCase(HttpStatusCode.Unauthorized, BrokerFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, BrokerFailure.Unavailable)]
    [TestCase(HttpStatusCode.BadRequest, BrokerFailure.BadResponse)]
    [TestCase(HttpStatusCode.NotFound, BrokerFailure.BadResponse)]
    [TestCase(HttpStatusCode.InternalServerError, BrokerFailure.BadResponse)]
    public async Task Status_MapsToItsFailure(HttpStatusCode status, BrokerFailure expected)
    {
        var result = await Feed(new StubHandler(status, "")).GetReportsAsync(Connection, null, 100, CancellationToken.None);

        (result.Failure, result.Page).ShouldBe(((BrokerFailure?)expected, (BrokerFeedPage?)null));
    }

    [TestCase("not json")]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("""{ "cursor": "w:1", "hasMore": false }""")]
    public async Task BodyItCannotRead_IsABadResponse(string body)
    {
        var result = await Feed(new StubHandler(HttpStatusCode.OK, body)).GetReportsAsync(Connection, null, 100, CancellationToken.None);

        result.Failure.ShouldBe(BrokerFailure.BadResponse);
    }

    [Test]
    public async Task ACursorOver500Characters_IsABadResponse()
    {
        var body = $$"""{ "items": [], "cursor": "{{new string('c', 501)}}", "hasMore": false }""";

        (await Feed(new StubHandler(HttpStatusCode.OK, body)).GetReportsAsync(Connection, null, 100, CancellationToken.None))
            .Failure.ShouldBe(BrokerFailure.BadResponse);
    }

    [Test]
    public async Task NoAnswerWithinTheTimeout_IsATimeout_AndARefusedConnectionIsUnreachable()
    {
        var slow = new StubHandler(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(10));
        var refused = Connection with { BaseUrl = new Uri("http://127.0.0.1:1") };

        (await Feed(slow).GetReportsAsync(Connection, null, 100, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Timeout);
        (await new SimulatorBrokerFeed(new Clients(new SocketsHttpHandler())).GetReportsAsync(refused, null, 100, CancellationToken.None))
            .Failure.ShouldBe(BrokerFailure.Unreachable);
    }

    [Test]
    public async Task Dispatcher_SendsToTheAdapterOfTheConnection_AndAnUnconfiguredOneIsUnreachable()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Page(Fill));
        var dispatcher = new BrokerFeedDispatcher(
            new ConfiguredBrokerConnections([Connection]), [Feed(handler)], NullLogger<BrokerFeedDispatcher>.Instance);

        (await dispatcher.GetReportsAsync("simulator", null, 100, CancellationToken.None)).Page.ShouldNotBeNull().Items.Count.ShouldBe(1);
        (await dispatcher.GetReportsAsync("other-broker", null, 100, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
    }

    private static SimulatorBrokerFeed Feed(HttpMessageHandler handler) => new(new Clients(handler));

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
