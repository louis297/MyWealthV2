using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.MarketData;

/// <summary>Simulated instruments R25: <c>SimulatorMarketPrices</c> against canned HTTP that follows R21.</summary>
public class SimulatorMarketPricesTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    private static readonly MarketDataConnectionSettings Connection = new(
        "simulator", "Simulator", ".SIM", new Uri("http://simulator:8082/"), Key, TimeoutSeconds: 1, PollMinutes: 1);

    [Test]
    public async Task AGoodBody_MapsToItems_AndSendsTheKeyToThePriceRoute()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "asOf": "2026-11-23T09:30:00Z",
              "items": [
                { "symbol": "ACME.SIM", "currency": "USD", "price": 101.3700, "date": "2026-11-23" },
                { "symbol": "KIWI.SIM", "currency": "NZD", "price": 4.96, "date": "not a date" }
              ]
            }
            """);

        var result = await Prices(handler).GetPricesAsync(Connection, CancellationToken.None);

        result.Failure.ShouldBeNull();
        result.Items.ShouldBe([
            new MarketPriceItem("ACME.SIM", "USD", 101.37m, new DateOnly(2026, 11, 23)),
            new MarketPriceItem("KIWI.SIM", "NZD", 4.96m, null)
        ]);
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Get);
        sent.RequestUri!.AbsoluteUri.ShouldBe("http://simulator:8082/party/market/prices");
        sent.Headers.GetValues("X-Api-Key").ShouldBe([Key]);
    }

    [TestCase(HttpStatusCode.Unauthorized, MarketDataFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, MarketDataFailure.Unavailable)]
    [TestCase(HttpStatusCode.NotFound, MarketDataFailure.BadResponse)]
    [TestCase(HttpStatusCode.InternalServerError, MarketDataFailure.BadResponse)]
    [TestCase(HttpStatusCode.Redirect, MarketDataFailure.BadResponse)]
    public async Task Status_MapsToItsFailure(HttpStatusCode status, MarketDataFailure expected)
    {
        var result = await Prices(new StubHandler(status, "")).GetPricesAsync(Connection, CancellationToken.None);

        result.Items.ShouldBeNull();
        result.Failure.ShouldBe(expected);
    }

    [TestCase("not json")]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("""{ "asOf": "2026-11-23T09:30:00Z" }""")]
    [TestCase("""{ "items": [ { "symbol": "ACME.SIM", "currency": "USD", "price": "101.37", "date": "2026-11-23" } ] }""")]
    public async Task ABodyItCannotRead_IsABadResponse(string body)
    {
        var result = await Prices(new StubHandler(HttpStatusCode.OK, body)).GetPricesAsync(Connection, CancellationToken.None);

        result.Items.ShouldBeNull();
        result.Failure.ShouldBe(MarketDataFailure.BadResponse);
    }

    [Test]
    public async Task NoAnswerWithinTheTimeout_IsATimeout()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(10));

        var result = await Prices(handler).GetPricesAsync(Connection, CancellationToken.None);

        result.Failure.ShouldBe(MarketDataFailure.Timeout);
    }

    [Test]
    public async Task ARefusedConnection_IsUnreachable()
    {
        var refused = Connection with { BaseUrl = new Uri("http://127.0.0.1:1") };

        var result = await new SimulatorMarketPrices(new Clients(new SocketsHttpHandler()))
            .GetPricesAsync(refused, CancellationToken.None);

        result.Failure.ShouldBe(MarketDataFailure.Unreachable);
    }

    [Test]
    public async Task Dispatcher_SendsToTheAdapterOfTheConnection_WithItsSettings()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "asOf": "2026-11-23T09:30:00Z", "items": [] }""");
        var dispatcher = new MarketPricesDispatcher(
            new ConfiguredMarketDataConnections([Connection]), [Prices(handler)], NullLogger<MarketPricesDispatcher>.Instance);

        var result = await dispatcher.GetPricesAsync("simulator", CancellationToken.None);

        result.Items.ShouldNotBeNull().ShouldBeEmpty();
        handler.Requests.Single().Headers.GetValues("X-Api-Key").ShouldBe([Key]);
    }

    [Test]
    public async Task Dispatcher_AConnectionNotConfiguredOrWithoutAnAdapter_IsUnreachable()
    {
        var dispatcher = new MarketPricesDispatcher(
            new ConfiguredMarketDataConnections([Connection]), [], NullLogger<MarketPricesDispatcher>.Instance);

        (await dispatcher.GetPricesAsync("other", CancellationToken.None)).Failure.ShouldBe(MarketDataFailure.Unreachable);
        (await dispatcher.GetPricesAsync("simulator", CancellationToken.None)).Failure.ShouldBe(MarketDataFailure.Unreachable);
    }

    private static SimulatorMarketPrices Prices(HttpMessageHandler handler) => new(new Clients(handler));

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
