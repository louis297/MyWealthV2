using System.Net;
using System.Text;
using System.Text.Json;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Broker;
using MyWealthV2.Infrastructure.Custodian;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Custodian;

/// <summary>In-kind transfers R73, R74: <c>SimulatorCustodianFeed</c> and <c>SimulatorCustodianTransfers</c> against
/// canned HTTP that follows R62, R63 and R69, and <c>safekeepingAccount</c> on the broker's order call (R47).</summary>
public class SimulatorCustodianAdaptersTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    private static readonly CustodianConnectionSettings Connection = new(
        "simulator", "Simulator", new Uri("http://simulator:8082"), Key, TimeoutSeconds: 1, PollMinutes: 1);

    [Test]
    public async Task FeedPage_MapsMovementsCursorAndPositions_AndSendsTheKeyTheAccountTheCursorAndTheLimit()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "items": [
                { "id": "5f0c-M12", "direction": "In", "kind": "Transfer", "symbol": "ACME.SIM", "currency": "USD", "quantity": 100,
                  "settlementDate": "2026-11-24", "instructionId": "6b1f0c2e", "tradeReference": null, "corporateActionId": null,
                  "counterpartyName": "Harbour Brokers", "counterpartyAccount": "HB-0044172", "reference": null },
                { "id": "5f0c-M13", "direction": "Out", "kind": "Trade", "symbol": "BETA.SIM", "currency": "USD", "quantity": 0.5,
                  "settlementDate": "2026-11-25", "instructionId": null, "tradeReference": "5f0c-R4", "corporateActionId": null,
                  "counterpartyName": "Simulator Broker", "counterpartyAccount": null, "reference": null }
              ],
              "cursor": "5f0c:13",
              "hasMore": true,
              "positions": [ { "symbol": "ACME.SIM", "currency": "USD", "quantity": 100 }, { "symbol": "BETA.SIM", "currency": "USD", "quantity": -0.5 } ]
            }
            """);

        var result = await Feed(handler).GetMovementsAsync(
            Connection, new CustodianFeedRequest("simulator", "SIM DEMO", "5f0c:10", 100), CancellationToken.None);

        result.Failure.ShouldBeNull();
        var page = result.Page.ShouldNotBeNull();
        (page.Cursor, page.HasMore).ShouldBe(("5f0c:13", true));
        page.Movements.ShouldBe([
            new CustodianFeedMovement("5f0c-M12", CustodyMovementDirection.In, CustodyMovementKind.Transfer, "ACME.SIM", "USD", 100m,
                new DateOnly(2026, 11, 24), "6b1f0c2e", null, null, "Harbour Brokers", "HB-0044172", null),
            new CustodianFeedMovement("5f0c-M13", CustodyMovementDirection.Out, CustodyMovementKind.Trade, "BETA.SIM", "USD", 0.5m,
                new DateOnly(2026, 11, 25), null, "5f0c-R4", null, "Simulator Broker", null, null)
        ]);
        page.Positions.ShouldBe([new CustodianFeedPosition("ACME.SIM", "USD", 100m), new CustodianFeedPosition("BETA.SIM", "USD", -0.5m)]);
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Get);
        sent.Uri.ShouldBe("http://simulator:8082/party/custodian/accounts/SIM%20DEMO/movements?after=5f0c%3A10&limit=100");
        sent.ApiKey.ShouldBe(Key);
    }

    [Test]
    public async Task AFirstRead_SendsNoCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "items": [], "cursor": null, "hasMore": false, "positions": [] }""");

        var result = await Feed(handler).GetMovementsAsync(Connection, new CustodianFeedRequest("simulator", "SIM-1", null, 100), CancellationToken.None);

        result.Page.ShouldNotBeNull().Positions.ShouldBeEmpty();
        handler.Requests.Single().Uri.ShouldBe("http://simulator:8082/party/custodian/accounts/SIM-1/movements?limit=100");
    }

    [Test]
    public async Task AnUnknownKindOrDirection_ComesThroughEmpty_AndAnUnreadableQuantityOrDateToo()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "items": [
                { "id": "M1", "direction": "Sideways", "kind": "Dividend", "symbol": "A", "currency": "USD", "quantity": 1,
                  "settlementDate": "24/11/2026" }
              ],
              "cursor": "c", "hasMore": false, "positions": []
            }
            """);

        var movement = (await Feed(handler).GetMovementsAsync(Connection, new CustodianFeedRequest("simulator", "S", null, 100), CancellationToken.None))
            .Page.ShouldNotBeNull().Movements.ShouldHaveSingleItem();

        (movement.Direction, movement.Kind, movement.SettlementDate).ShouldBe(((CustodyMovementDirection?)null, (CustodyMovementKind?)null, (DateOnly?)null));
    }

    [Test]
    public async Task AQuantityThatIsNotAJsonNumber_ComesThroughEmpty_SoThePullRefusesThePage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            { "items": [ { "id": "M1", "direction": "In", "kind": "Transfer", "symbol": "A", "currency": "USD", "quantity": "1",
              "settlementDate": "2026-11-24" } ], "cursor": "c", "hasMore": false,
              "positions": [ { "symbol": "A", "currency": "USD", "quantity": "1" } ] }
            """);

        var page = (await Feed(handler).GetMovementsAsync(Connection, new CustodianFeedRequest("simulator", "S", null, 100), CancellationToken.None))
            .Page.ShouldNotBeNull();

        page.Movements.ShouldHaveSingleItem().Quantity.ShouldBeNull();
        page.Positions.ShouldNotBeNull().ShouldHaveSingleItem().Quantity.ShouldBeNull();
    }

    [Test]
    public async Task APageWithNoPositions_ComesThroughWithNone()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "items": [], "cursor": "c", "hasMore": false }""");

        (await Feed(handler).GetMovementsAsync(Connection, new CustodianFeedRequest("simulator", "S", null, 100), CancellationToken.None))
            .Page.ShouldNotBeNull().Positions.ShouldBeNull();
    }

    [TestCase(HttpStatusCode.Unauthorized, CustodianFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, CustodianFailure.Unavailable)]
    [TestCase(HttpStatusCode.NotFound, CustodianFailure.UnknownAccount)]
    [TestCase(HttpStatusCode.BadRequest, CustodianFailure.BadResponse)]
    [TestCase(HttpStatusCode.InternalServerError, CustodianFailure.BadResponse)]
    public async Task AFeedStatus_MapsToItsFailure(HttpStatusCode status, CustodianFailure failure) =>
        (await Feed(new StubHandler(status, "{}")).GetMovementsAsync(Connection, new CustodianFeedRequest("simulator", "S", null, 100),
            CancellationToken.None)).Failure.ShouldBe(failure);

    [Test]
    public async Task AnUnreadableBody_OrACursorOver500_IsABadResponse()
    {
        (await Feed(new StubHandler(HttpStatusCode.OK, "not json")).GetMovementsAsync(Connection,
            new CustodianFeedRequest("simulator", "S", null, 100), CancellationToken.None)).Failure.ShouldBe(CustodianFailure.BadResponse);
        var longCursor = JsonSerializer.Serialize(new string('c', 501));
        (await Feed(new StubHandler(HttpStatusCode.OK, $$"""{ "items": [], "cursor": {{longCursor}}, "hasMore": false, "positions": [] }"""))
            .GetMovementsAsync(Connection, new CustodianFeedRequest("simulator", "S", null, 100), CancellationToken.None))
            .Failure.ShouldBe(CustodianFailure.BadResponse);
    }

    [Test]
    public async Task ASlowParty_IsATimeout_AndNoConnectionIsUnreachable()
    {
        (await Feed(new StubHandler(HttpStatusCode.OK, "{}", TimeSpan.FromSeconds(5))).GetMovementsAsync(Connection,
            new CustodianFeedRequest("simulator", "S", null, 100), CancellationToken.None)).Failure.ShouldBe(CustodianFailure.Timeout);
        (await Feed(new ThrowingHandler()).GetMovementsAsync(Connection, new CustodianFeedRequest("simulator", "S", null, 100),
            CancellationToken.None)).Failure.ShouldBe(CustodianFailure.Unreachable);
    }

    [Test]
    public async Task ATransfer_IsPostedWithItsBody_AndAcceptedMapsToTheCustodiansId()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "transferId": "5f0c-T3", "status": "Accepted" }""");
        var instruction = Guid.Parse("6b1f0c2e-0000-4000-8000-000000000001");

        var result = await Transfers(handler).SendAsync(Connection, new CustodianTransferRequest(
            "simulator", "SIM-DEMO-CUSTODY-USD", instruction, TransferDirection.Out, "ACME.SIM", "USD", 40m,
            new CustodianCounterparty("Harbour Brokers", "HB-0044172", "J Smith")), CancellationToken.None);

        result.TransferId.ShouldBe("5f0c-T3");
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Uri.ShouldBe("http://simulator:8082/party/custodian/accounts/SIM-DEMO-CUSTODY-USD/transfers");
        sent.ApiKey.ShouldBe(Key);
        using var body = JsonDocument.Parse(sent.Body!);
        body.RootElement.GetProperty("instructionId").GetString().ShouldBe(instruction.ToString());
        body.RootElement.GetProperty("direction").GetString().ShouldBe("Out");
        body.RootElement.GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        body.RootElement.GetProperty("currency").GetString().ShouldBe("USD");
        body.RootElement.GetProperty("quantity").GetDecimal().ShouldBe(40m);
        body.RootElement.GetProperty("counterparty").GetProperty("holderName").GetString().ShouldBe("J Smith");
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["instructionId", "direction", "symbol", "currency", "quantity", "counterparty"], ignoreOrder: true);
    }

    [TestCase("InsufficientPosition", TransferRejectReason.InsufficientPosition)]
    [TestCase("UnknownInstrument", TransferRejectReason.UnknownInstrument)]
    [TestCase("Something", TransferRejectReason.Refused)]
    public async Task Rejected_MapsTheReason_AndAnUnknownOneIsRefused(string reason, TransferRejectReason expected) =>
        (await Transfers(new StubHandler(HttpStatusCode.OK, $$"""{ "transferId": "T1", "status": "Rejected", "reason": "{{reason}}" }"""))
            .SendAsync(Connection, Request(), CancellationToken.None)).RejectReason.ShouldBe(expected);

    [TestCase(HttpStatusCode.NotFound, CustodianFailure.UnknownAccount)]
    [TestCase(HttpStatusCode.Conflict, CustodianFailure.BadResponse)]
    [TestCase(HttpStatusCode.Unauthorized, CustodianFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, CustodianFailure.Unavailable)]
    public async Task ATransferStatus_MapsToItsFailure(HttpStatusCode status, CustodianFailure failure) =>
        (await Transfers(new StubHandler(status, "{}")).SendAsync(Connection, Request(), CancellationToken.None)).Failure.ShouldBe(failure);

    [Test]
    public async Task AnAnswerItCannotRead_IsABadResponse() =>
        (await Transfers(new StubHandler(HttpStatusCode.OK, """{ "status": "Maybe" }""")).SendAsync(Connection, Request(), CancellationToken.None))
            .Failure.ShouldBe(CustodianFailure.BadResponse);

    [TestCase("SIM-DEMO-CUSTODY-USD", true)]
    [TestCase(null, false)]
    public async Task TheBrokersOrderCall_CarriesTheSafekeepingAccount_OnlyWhenTheOrderHasOne(string? safekeeping, bool present)
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "brokerOrderId": "B1", "status": "Accepted" }""");
        var broker = new SimulatorBrokerOrders(new Clients(handler));

        await broker.PlaceAsync(new BrokerConnectionSettings("simulator", "Simulator", new Uri("http://simulator:8082"), Key, 1, 1),
            new BrokerOrderRequest(Guid.NewGuid().ToString(), "ACME.SIM", "USD", OrderSide.Buy, 1m, OrderType.Market, TimeInForce.Day,
                105m, 100m, null, safekeeping), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        body.RootElement.TryGetProperty("safekeepingAccount", out var value).ShouldBe(present);
        if (present)
        {
            value.GetString().ShouldBe(safekeeping);
        }
    }

    [Test]
    public async Task AnElection_IsPostedWithItsBody_AndAcceptedMapsToAccepted()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "status": "Accepted" }""");
        var instruction = Guid.Parse("6b1f0c2e-0000-4000-8000-000000000003");

        var result = await Elections(handler).SubmitAsync(Connection, new CustodianElectionRequest(
            "simulator", "SIM-DEMO-CUSTODY-USD", instruction, "0f8f-CA3", "ACME.SIM", "USD", 78.82352941m), CancellationToken.None);

        (result.IsAccepted, result.RejectReason, result.Failure).ShouldBe((true, (ElectionRejectReason?)null, (CustodianFailure?)null));
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Uri.ShouldBe("http://simulator:8082/party/custodian/accounts/SIM-DEMO-CUSTODY-USD/elections");
        sent.ApiKey.ShouldBe(Key);
        using var body = JsonDocument.Parse(sent.Body!);
        body.RootElement.GetProperty("instructionId").GetString().ShouldBe(instruction.ToString());
        body.RootElement.GetProperty("corporateActionId").GetString().ShouldBe("0f8f-CA3");
        body.RootElement.GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        body.RootElement.GetProperty("currency").GetString().ShouldBe("USD");
        body.RootElement.GetProperty("quantity").GetDecimal().ShouldBe(78.82352941m);
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["instructionId", "corporateActionId", "symbol", "currency", "quantity"], ignoreOrder: true);
    }

    [TestCase("UnknownCorporateAction", ElectionRejectReason.UnknownCorporateAction)]
    [TestCase("NoPlan", ElectionRejectReason.NoPlan)]
    [TestCase("NotEntitled", ElectionRejectReason.NotEntitled)]
    [TestCase("TooLate", ElectionRejectReason.TooLate)]
    [TestCase("Something", ElectionRejectReason.Other)]
    public async Task ARejectedElection_MapsTheReason_AndAnUnknownOneIsOther(string reason, ElectionRejectReason expected) =>
        (await Elections(new StubHandler(HttpStatusCode.OK, $$"""{ "status": "Rejected", "reason": "{{reason}}" }"""))
            .SubmitAsync(Connection, Election(), CancellationToken.None)).RejectReason.ShouldBe(expected);

    [TestCase(HttpStatusCode.NotFound, CustodianFailure.UnknownAccount)]
    [TestCase(HttpStatusCode.Conflict, CustodianFailure.BadResponse)]
    [TestCase(HttpStatusCode.Unauthorized, CustodianFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, CustodianFailure.Unavailable)]
    public async Task AnElectionStatus_MapsToItsFailure(HttpStatusCode status, CustodianFailure failure) =>
        (await Elections(new StubHandler(status, "{}")).SubmitAsync(Connection, Election(), CancellationToken.None)).Failure.ShouldBe(failure);

    [Test]
    public async Task AnElection_ThatIsSlowOrUnreadable_Fails()
    {
        (await Elections(new StubHandler(HttpStatusCode.OK, "{}", TimeSpan.FromSeconds(5))).SubmitAsync(Connection, Election(),
            CancellationToken.None)).Failure.ShouldBe(CustodianFailure.Timeout);
        (await Elections(new StubHandler(HttpStatusCode.OK, "not json")).SubmitAsync(Connection, Election(), CancellationToken.None))
            .Failure.ShouldBe(CustodianFailure.BadResponse);
        (await Elections(new ThrowingHandler()).SubmitAsync(Connection, Election(), CancellationToken.None))
            .Failure.ShouldBe(CustodianFailure.Unreachable);
    }

    private static CustodianElectionRequest Election() => new("simulator", "S", Guid.NewGuid(), "W-CA1", "A", "USD", 1m);

    private static SimulatorCustodianElections Elections(HttpMessageHandler handler) => new(new Clients(handler));

    private static CustodianTransferRequest Request() =>
        new("simulator", "S", Guid.NewGuid(), TransferDirection.In, "A", "USD", 1m, new CustodianCounterparty("H", "1", "J"));

    private static SimulatorCustodianFeed Feed(HttpMessageHandler handler) => new(new Clients(handler));

    private static SimulatorCustodianTransfers Transfers(HttpMessageHandler handler) => new(new Clients(handler));

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record Sent(HttpMethod Method, string Uri, string? ApiKey, string? Body);

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? delay = null) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Sent(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection");
    }
}
