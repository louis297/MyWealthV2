using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Broker;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Broker;

/// <summary>Orders R53, R59, R66: <c>SimulatorBrokerOrders</c> against canned HTTP.</summary>
public class SimulatorBrokerOrdersTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";
    private const string ClientOrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";

    private static readonly BrokerConnectionSettings Connection = new(
        "simulator", "Simulator", new Uri("http://simulator:8082/"), Key, TimeoutSeconds: 1, PollMinutes: 1);

    private static readonly BrokerOrderRequest Order = new(
        ClientOrderId, "ACME.SIM", "USD", OrderSide.Buy, 10m, OrderType.Market, TimeInForce.Day, 105.00m, 100.00m);

    [Test]
    public async Task Accepted_ReturnsTheBrokersOrderId_AndSendsTheBodyAndTheKeyToTheOrderRoute()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "brokerOrderId": "0f8f-O1", "status": "Accepted" }""");

        var result = await Orders(handler).PlaceAsync(Connection, Order, CancellationToken.None);

        (result.BrokerOrderId, result.RejectReason, result.Failure).ShouldBe(("0f8f-O1", (OrderRejectReason?)null, (BrokerFailure?)null));
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri!.AbsoluteUri.ShouldBe("http://simulator:8082/party/broker/orders");
        sent.Headers.GetValues("X-Api-Key").ShouldBe([Key]);
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe([
            "clientOrderId", "symbol", "currency", "side", "quantity", "type", "timeInForce", "priceLimit", "referencePrice"
        ], ignoreOrder: true);
        root.GetProperty("clientOrderId").GetString().ShouldBe(ClientOrderId);
        root.GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        root.GetProperty("currency").GetString().ShouldBe("USD");
        root.GetProperty("side").GetString().ShouldBe("Buy");
        root.GetProperty("quantity").ValueKind.ShouldBe(JsonValueKind.Number);
        root.GetProperty("quantity").GetDecimal().ShouldBe(10m);
        root.GetProperty("type").GetString().ShouldBe("Market");
        root.GetProperty("timeInForce").GetString().ShouldBe("Day");
        root.GetProperty("priceLimit").GetDecimal().ShouldBe(105.00m);
        root.GetProperty("referencePrice").GetDecimal().ShouldBe(100.00m);
    }

    [Test]
    public async Task AnOrderWithASettlementAccount_SendsIt_AndOneWithoutLeavesTheFieldOut()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "brokerOrderId": "0f8f-O1", "status": "Accepted" }""");

        await Orders(handler).PlaceAsync(Connection, Order with { SettlementAccount = "SIM-DEMO-USD" }, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        body.RootElement.TryGetProperty("settlementAccount", out var settlementAccount).ShouldBeTrue();
        settlementAccount.GetString().ShouldBe("SIM-DEMO-USD");
        body.RootElement.EnumerateObject().Count().ShouldBe(10);

        var without = new StubHandler(HttpStatusCode.OK, """{ "brokerOrderId": "0f8f-O1", "status": "Accepted" }""");
        await Orders(without).PlaceAsync(Connection, Order, CancellationToken.None);
        using var plain = JsonDocument.Parse(without.Bodies.Single());
        plain.RootElement.TryGetProperty("settlementAccount", out _).ShouldBeFalse();
    }

    [TestCase("UnknownInstrument", OrderRejectReason.UnknownInstrument)]
    [TestCase("InvalidOrder", OrderRejectReason.InvalidOrder)]
    [TestCase("MarketClosed", OrderRejectReason.MarketClosed)]
    [TestCase("Refused", OrderRejectReason.Refused)]
    [TestCase("SomethingNew", OrderRejectReason.Refused)]
    [TestCase("1", OrderRejectReason.Refused)]
    [TestCase(null, OrderRejectReason.Refused)]
    public async Task Rejected_MapsTheReason_AndAnUnknownOneIsRefused(string? reason, OrderRejectReason expected)
    {
        var json = reason is null ? """{ "status": "Rejected" }""" : $$"""{ "status": "Rejected", "reason": "{{reason}}" }""";

        var result = await Orders(new StubHandler(HttpStatusCode.OK, json)).PlaceAsync(Connection, Order, CancellationToken.None);

        (result.RejectReason, result.BrokerOrderId, result.Failure).ShouldBe(((OrderRejectReason?)expected, (string?)null, (BrokerFailure?)null));
    }

    [TestCase(HttpStatusCode.Unauthorized, BrokerFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, BrokerFailure.Unavailable)]
    [TestCase(HttpStatusCode.BadRequest, BrokerFailure.BadResponse)]
    [TestCase(HttpStatusCode.Conflict, BrokerFailure.BadResponse)]
    [TestCase(HttpStatusCode.NotFound, BrokerFailure.BadResponse)]
    [TestCase(HttpStatusCode.InternalServerError, BrokerFailure.BadResponse)]
    public async Task Place_Status_MapsToItsFailure(HttpStatusCode status, BrokerFailure expected)
    {
        var result = await Orders(new StubHandler(status, "")).PlaceAsync(Connection, Order, CancellationToken.None);

        (result.Failure, result.BrokerOrderId, result.RejectReason).ShouldBe(((BrokerFailure?)expected, (string?)null, (OrderRejectReason?)null));
    }

    [TestCase("not json")]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("""{ "status": "Accepted" }""")]
    [TestCase("""{ "brokerOrderId": "", "status": "Accepted" }""")]
    [TestCase("""{ "brokerOrderId": "O1", "status": "Maybe" }""")]
    public async Task Place_BodyItCannotRead_IsABadResponse(string body)
    {
        var result = await Orders(new StubHandler(HttpStatusCode.OK, body)).PlaceAsync(Connection, Order, CancellationToken.None);

        result.Failure.ShouldBe(BrokerFailure.BadResponse);
    }

    [Test]
    public async Task NoAnswerWithinTheTimeout_IsATimeout()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(10));

        (await Orders(handler).PlaceAsync(Connection, Order, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Timeout);
        (await Orders(handler).CancelAsync(Connection, ClientOrderId, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Timeout);
    }

    [Test]
    public async Task RefusedConnection_IsUnreachable()
    {
        var refused = Connection with { BaseUrl = new Uri("http://127.0.0.1:1") };
        var orders = new SimulatorBrokerOrders(new Clients(new SocketsHttpHandler()));

        (await orders.PlaceAsync(refused, Order, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
        (await orders.CancelAsync(refused, ClientOrderId, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
    }

    [TestCase("Accepted", BrokerCancelAnswer.Accepted)]
    [TestCase("TooLate", BrokerCancelAnswer.TooLate)]
    public async Task Cancel_ReturnsTheBrokersAnswer_AndCallsTheCancelRouteOfTheClientOrderId(string status, BrokerCancelAnswer expected)
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{ "status": "{{status}}" }""");

        var result = await Orders(handler).CancelAsync(Connection, ClientOrderId, CancellationToken.None);

        (result.Answer, result.Failure).ShouldBe(((BrokerCancelAnswer?)expected, (BrokerFailure?)null));
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri!.AbsoluteUri.ShouldBe($"http://simulator:8082/party/broker/orders/{ClientOrderId}/cancel");
        sent.Headers.GetValues("X-Api-Key").ShouldBe([Key]);
    }

    [Test]
    public async Task Cancel_404_IsUnknownOrder_AnAnswerAndNotAFailure()
    {
        var result = await Orders(new StubHandler(HttpStatusCode.NotFound, "")).CancelAsync(Connection, ClientOrderId, CancellationToken.None);

        (result.Answer, result.Failure).ShouldBe(((BrokerCancelAnswer?)BrokerCancelAnswer.UnknownOrder, (BrokerFailure?)null));
    }

    [TestCase(HttpStatusCode.Unauthorized, "", BrokerFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, "", BrokerFailure.Unavailable)]
    [TestCase(HttpStatusCode.BadRequest, "", BrokerFailure.BadResponse)]
    [TestCase(HttpStatusCode.OK, "not json", BrokerFailure.BadResponse)]
    [TestCase(HttpStatusCode.OK, """{ "status": "Maybe" }""", BrokerFailure.BadResponse)]
    public async Task Cancel_AStatusOrBodyItCannotRead_MapsToItsFailure(HttpStatusCode status, string body, BrokerFailure expected)
    {
        var result = await Orders(new StubHandler(status, body)).CancelAsync(Connection, ClientOrderId, CancellationToken.None);

        (result.Failure, result.Answer).ShouldBe(((BrokerFailure?)expected, (BrokerCancelAnswer?)null));
    }

    [Test]
    public async Task Dispatcher_SendsToTheAdapterOfTheConnection_AndAnUnconfiguredOneIsUnreachable()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "brokerOrderId": "0f8f-O1", "status": "Accepted" }""");
        var dispatcher = new BrokerOrdersDispatcher(
            new ConfiguredBrokerConnections([Connection]), [Orders(handler)], NullLogger<BrokerOrdersDispatcher>.Instance);

        (await dispatcher.PlaceAsync("simulator", Order, CancellationToken.None)).BrokerOrderId.ShouldBe("0f8f-O1");
        (await dispatcher.PlaceAsync("other-broker", Order, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
        (await dispatcher.CancelAsync("other-broker", ClientOrderId, CancellationToken.None)).Failure.ShouldBe(BrokerFailure.Unreachable);
    }

    private static SimulatorBrokerOrders Orders(HttpMessageHandler handler) => new(new Clients(handler));

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? delay = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
