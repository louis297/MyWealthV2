using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.BankFeed;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.BankFeed;

/// <summary>Deposit matching R49: <c>SimulatorBankFeed</c> against canned HTTP that follows R44 and R45.</summary>
public class SimulatorBankFeedTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    private static readonly BankConnectionSettings Connection = new(
        "simulator", "Simulator", new Uri("http://simulator:8082"), Key, TimeoutSeconds: 1, PollMinutes: 1);

    [Test]
    public async Task Page_MapsToLinesAndCursor_AndSendsTheKeyTheAccountTheCursorAndTheLimit()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "items": [
                { "id": "3f2c-L7", "valueDate": "2026-11-20", "direction": "Credit", "amount": 500.00, "currency": "USD",
                  "payerName": "J SMITH", "payerAccount": "12-3456-7890042-00", "reference": "K7M 3Q9 PXB" },
                { "id": "3f2c-L8", "valueDate": "2026-11-19", "direction": "Debit", "amount": 0.0001, "currency": "USD",
                  "payerName": null, "payerAccount": null, "reference": null }
              ],
              "cursor": "3f2c:12",
              "hasMore": true
            }
            """);

        var result = await Feed(handler).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO USD", "3f2c:10", 100), CancellationToken.None);

        result.Failure.ShouldBeNull();
        var page = result.Page.ShouldNotBeNull();
        page.Cursor.ShouldBe("3f2c:12");
        page.HasMore.ShouldBeTrue();
        page.Lines.ShouldBe([
            new BankFeedLine("3f2c-L7", new DateOnly(2026, 11, 20), BankLineDirection.Credit, 500.00m, "USD",
                "J SMITH", "12-3456-7890042-00", "K7M 3Q9 PXB"),
            new BankFeedLine("3f2c-L8", new DateOnly(2026, 11, 19), BankLineDirection.Debit, 0.0001m, "USD",
                null, null, null)
        ]);
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Get);
        sent.RequestUri!.AbsoluteUri.ShouldBe(
            "http://simulator:8082/party/bank/accounts/SIM-DEMO%20USD/statement-lines?after=3f2c%3A10&limit=100");
        sent.Headers.GetValues("X-Api-Key").ShouldBe([Key]);
    }

    [Test]
    public async Task PaymentLines_CarryTheEndToEndIdTheReturnedLineAndTheReason()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "items": [
                { "id": "3f2c-L12", "valueDate": "2026-11-20", "direction": "Debit", "amount": 300.00, "currency": "USD",
                  "payerName": "J Smith", "payerAccount": "06-0123-0456789-00", "reference": "K7M3Q9PXB",
                  "endToEndId": "6b1f0c2e", "returnOf": null, "returnReason": null },
                { "id": "3f2c-L13", "valueDate": "2026-11-21", "direction": "Credit", "amount": 300.00, "currency": "USD",
                  "payerName": "J Smith", "payerAccount": "06-0123-0456789-00", "reference": "K7M3Q9PXB",
                  "endToEndId": null, "returnOf": "3f2c-L12", "returnReason": "Account closed" }
              ],
              "cursor": "3f2c:13",
              "hasMore": false
            }
            """);

        var result = await Feed(handler).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        var lines = result.Page!.Lines;
        lines[0].EndToEndId.ShouldBe("6b1f0c2e");
        lines[0].ReturnOf.ShouldBeNull();
        lines[0].Direction.ShouldBe(BankLineDirection.Debit);
        lines[1].EndToEndId.ShouldBeNull();
        lines[1].ReturnOf.ShouldBe("3f2c-L12");
        lines[1].ReturnReason.ShouldBe("Account closed");
    }

    [Test]
    public async Task FirstRead_SendsNoCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "items": [], "cursor": "w:0", "hasMore": false }""");

        var result = await Feed(handler).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Page!.Lines.ShouldBeEmpty();
        handler.Requests.Single().RequestUri!.PathAndQuery.ShouldBe("/party/bank/accounts/SIM-DEMO-USD/statement-lines?limit=100");
    }

    [Test]
    public async Task AFieldItCannotRead_IsLeftEmpty_SoThePullRefusesThePage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            { "items": [ { "id": "L1", "valueDate": "20 Nov", "direction": "Sideways", "amount": 1, "currency": "USD" } ],
              "cursor": "w:1", "hasMore": false }
            """);

        var result = await Feed(handler).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        var line = result.Page!.Lines.ShouldHaveSingleItem();
        line.ValueDate.ShouldBeNull();
        line.Direction.ShouldBeNull();
        line.Amount.ShouldBe(1m);
    }

    [TestCase("12512.40", 12512.40)]
    [TestCase("-15.0001", -15.0001)]
    [TestCase("0", 0)]
    public async Task Balance_IsReadAsANumber(string balance, decimal expected)
    {
        // Bank-reconciliation R31: the bank's balance after the page's last line.
        var handler = new StubHandler(HttpStatusCode.OK,
            $$"""{ "items": [], "cursor": "w:4", "hasMore": false, "balance": {{balance}} }""");

        var result = await Feed(handler).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Page!.Balance.ShouldBe(expected);
    }

    [TestCase("""{ "items": [], "cursor": "w:4", "hasMore": false, "balance": null }""")]
    [TestCase("""{ "items": [], "cursor": "w:4", "hasMore": false }""")]
    public async Task NoBalance_IsNull(string body)
    {
        var result = await Feed(new StubHandler(HttpStatusCode.OK, body)).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Page.ShouldNotBeNull();
        result.Page.Balance.ShouldBeNull();
    }

    [TestCase("""{ "items": [], "cursor": "w:4", "hasMore": false, "balance": "12512.40" }""")]
    [TestCase("""{ "items": [], "cursor": "w:4", "hasMore": false, "balance": 12512.40001 }""")]
    public async Task ABalanceItCannotRead_IsABadResponse(string body)
    {
        var result = await Feed(new StubHandler(HttpStatusCode.OK, body)).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Page.ShouldBeNull();
        result.Failure.ShouldBe(BankFeedFailure.BadResponse);
    }

    [TestCase(HttpStatusCode.Unauthorized, BankFeedFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, BankFeedFailure.Unavailable)]
    [TestCase(HttpStatusCode.NotFound, BankFeedFailure.UnknownAccount)]
    [TestCase(HttpStatusCode.InternalServerError, BankFeedFailure.BadResponse)]
    [TestCase(HttpStatusCode.BadRequest, BankFeedFailure.BadResponse)]
    [TestCase(HttpStatusCode.Redirect, BankFeedFailure.BadResponse)]
    public async Task Status_MapsToItsFailure(HttpStatusCode status, BankFeedFailure expected)
    {
        var result = await Feed(new StubHandler(status, "")).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Page.ShouldBeNull();
        result.Failure.ShouldBe(expected);
    }

    [TestCase("not json")]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("""{ "cursor": "w:1", "hasMore": false }""")]
    [TestCase("""{ "items": [ { "id": "L1", "amount": "500.00" } ], "cursor": "w:1", "hasMore": false }""")]
    public async Task BodyItCannotRead_IsABadResponse(string body)
    {
        var result = await Feed(new StubHandler(HttpStatusCode.OK, body)).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Failure.ShouldBe(BankFeedFailure.BadResponse);
    }

    [Test]
    public async Task NoAnswerWithinTheTimeout_IsATimeout()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(10));

        var result = await Feed(handler).GetStatementLinesAsync(
            Connection, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Failure.ShouldBe(BankFeedFailure.Timeout);
    }

    [Test]
    public async Task RefusedConnection_IsUnreachable()
    {
        var refused = Connection with { BaseUrl = new Uri("http://127.0.0.1:1") };

        var result = await new SimulatorBankFeed(new Clients(new SocketsHttpHandler())).GetStatementLinesAsync(
            refused, new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Failure.ShouldBe(BankFeedFailure.Unreachable);
    }

    [Test]
    public async Task Dispatcher_SendsToTheAdapterOfTheConnection_WithItsSettings()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "items": [], "cursor": "w:0", "hasMore": false }""");
        var dispatcher = new BankFeedDispatcher(
            new ConfiguredBankConnections([Connection]), [Feed(handler)], NullLogger<BankFeedDispatcher>.Instance);

        var result = await dispatcher.GetStatementLinesAsync(
            new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None);

        result.Page.ShouldNotBeNull();
        handler.Requests.Single().Headers.GetValues("X-Api-Key").ShouldBe([Key]);
    }

    [Test]
    public async Task Dispatcher_ConnectionNotConfiguredOnThisHost_IsUnreachable()
    {
        var dispatcher = new BankFeedDispatcher(
            new ConfiguredBankConnections([Connection]), [], NullLogger<BankFeedDispatcher>.Instance);

        (await dispatcher.GetStatementLinesAsync(
            new BankFeedRequest("other-bank", "SIM-DEMO-USD", null, 100), CancellationToken.None))
            .Failure.ShouldBe(BankFeedFailure.Unreachable);
        (await dispatcher.GetStatementLinesAsync(
            new BankFeedRequest("simulator", "SIM-DEMO-USD", null, 100), CancellationToken.None))
            .Failure.ShouldBe(BankFeedFailure.Unreachable);
    }

    private static SimulatorBankFeed Feed(HttpMessageHandler handler) => new(new Clients(handler));

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? delay = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
