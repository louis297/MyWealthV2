using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.MarketData;

public class TwelveDataClientTests
{
    [Test]
    public void For_KnownSymbols_UseTheExchangeSuffix()
    {
        TwelveDataSymbols.For("aia", "nzd").ShouldBe("AIA:NZX");
        TwelveDataSymbols.For("FPH", "NZD").ShouldBe("FPH:NZX");
        TwelveDataSymbols.For("MEL", "NZD").ShouldBe("MEL:NZX");
        TwelveDataSymbols.For("BHP", "AUD").ShouldBe("BHP:ASX");
        TwelveDataSymbols.For("CBA", "AUD").ShouldBe("CBA:ASX");
        TwelveDataSymbols.For("WOW", "AUD").ShouldBe("WOW:ASX");
        TwelveDataSymbols.For("SAP", "EUR").ShouldBe("SAP:XETR");
        TwelveDataSymbols.For("AIR", "EUR").ShouldBe("AIR:EPA");
        TwelveDataSymbols.For("OR", "EUR").ShouldBe("OR:EPA");
        TwelveDataSymbols.For("SHEL", "GBP").ShouldBe("SHEL:LSE");
        TwelveDataSymbols.For("HSBA", "GBP").ShouldBe("HSBA:LSE");
        TwelveDataSymbols.For("ULVR", "GBP").ShouldBe("ULVR:LSE");
        TwelveDataSymbols.For("7203", "JPY").ShouldBe("7203:TSE");
        TwelveDataSymbols.For("6758", "JPY").ShouldBe("6758:TSE");
        TwelveDataSymbols.For("9984", "JPY").ShouldBe("9984:TSE");
        TwelveDataSymbols.For("AAPL", "USD").ShouldBe("AAPL");
        TwelveDataSymbols.For("VTI", "USD").ShouldBe("VTI");
        TwelveDataSymbols.For("MSFT", "USD").ShouldBe("MSFT");
        TwelveDataSymbols.For("NEW", "USD").ShouldBe("NEW");
    }

    [Test]
    public async Task GetLatest_RequestsEodAndReadsTheClose()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "symbol": "AAPL",
              "currency": "USD",
              "datetime": "2026-11-02",
              "close": "180.12500000"
            }
            """);
        var client = Client(handler, "test-key");

        var price = await client.GetLatestAsync("AAPL", "USD");

        handler.Urls.ShouldBe(["/eod?symbol=AAPL&dp=8&apikey=test-key"]);
        price.ShouldBe(new MyWealthV2.Application.MarketData.SourcedInstrumentPrice(
            180.12500000m, new DateOnly(2026, 11, 2), "USD"));
    }

    [Test]
    public async Task GetLatest_UsesTheExchangeSuffix()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "symbol": "AIA:NZX",
              "currency": "NZD",
              "datetime": "2026-11-01 00:00:00",
              "close": "8.5"
            }
            """);
        var client = Client(handler, "test-key");

        var price = await client.GetLatestAsync("AIA", "NZD");

        handler.Urls.ShouldBe(["/eod?symbol=AIA%3ANZX&dp=8&apikey=test-key"]);
        price.ShouldNotBeNull();
        price.Value.AsOfDate.ShouldBe(new DateOnly(2026, 11, 1));
        price.Value.Price.ShouldBe(8.5m);
        price.Value.Currency.ShouldBe("NZD");
    }

    [Test]
    public async Task GetLatest_ErrorPayload_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"code":400,"message":"symbol not found","status":"error"}
            """);
        var client = Client(handler, "test-key");

        (await client.GetLatestAsync("NOPE", "USD")).ShouldBeNull();

        handler.StatusCode = HttpStatusCode.BadRequest;
        (await client.GetLatestAsync("NOPE", "USD")).ShouldBeNull();
    }

    [Test]
    public async Task GetLatest_ServerError_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, """{"status":"error"}""");
        var client = Client(handler, "test-key");

        await Should.ThrowAsync<HttpRequestException>(() => client.GetLatestAsync("AAPL", "USD"));
    }

    [Test]
    public async Task GetLatest_MissingKey_ThrowsBeforeCalling()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = Client(handler, "");

        await Should.ThrowAsync<InvalidOperationException>(() => client.GetLatestAsync("AAPL", "USD"));
        handler.Urls.ShouldBeEmpty();
    }

    private static TwelveDataClient Client(StubHandler handler, string apiKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TwelveData:ApiKey"] = apiKey })
            .Build();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.twelvedata.com") };
        return new TwelveDataClient(http, configuration);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string json) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        public HttpStatusCode StatusCode { get; set; } = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
