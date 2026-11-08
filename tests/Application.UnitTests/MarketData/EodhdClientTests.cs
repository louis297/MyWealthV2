using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.MarketData;

public class EodhdClientTests
{
    [Test]
    public void For_KnownSymbols_UseTheExchangeSuffix()
    {
        EodhdSymbols.For("aia", "nzd").ShouldBe("AIA.NZ");
        EodhdSymbols.For("FPH", "NZD").ShouldBe("FPH.NZ");
        EodhdSymbols.For("MEL", "NZD").ShouldBe("MEL.NZ");
        EodhdSymbols.For("BHP", "AUD").ShouldBe("BHP.AU");
        EodhdSymbols.For("CBA", "AUD").ShouldBe("CBA.AU");
        EodhdSymbols.For("WOW", "AUD").ShouldBe("WOW.AU");
        EodhdSymbols.For("SAP", "EUR").ShouldBe("SAP.XETRA");
        EodhdSymbols.For("AIR", "EUR").ShouldBe("AIR.PA");
        EodhdSymbols.For("OR", "EUR").ShouldBe("OR.PA");
        EodhdSymbols.For("SHEL", "GBP").ShouldBe("SHEL.LSE");
        EodhdSymbols.For("HSBA", "GBP").ShouldBe("HSBA.LSE");
        EodhdSymbols.For("ULVR", "GBP").ShouldBe("ULVR.LSE");
        EodhdSymbols.For("7203", "JPY").ShouldBe("7203.TSE");
        EodhdSymbols.For("6758", "JPY").ShouldBe("6758.TSE");
        EodhdSymbols.For("9984", "JPY").ShouldBe("9984.TSE");
        EodhdSymbols.For("AAPL", "USD").ShouldBe("AAPL.US");
        EodhdSymbols.For("VTI", "USD").ShouldBe("VTI.US");
        EodhdSymbols.For("MSFT", "USD").ShouldBe("MSFT.US");
        EodhdSymbols.For("NEW", "USD").ShouldBe("NEW");
    }

    [Test]
    public async Task GetLatest_RequestsEodAndReadsTheLatestClose()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [
              {"date":"2026-11-01","close":100.0},
              {"date":"2026-11-03","close":180.125},
              {"date":"2026-11-02","close":170.0}
            ]
            """);
        var client = Client(handler, "test-key");

        var price = await client.GetLatestAsync("AAPL", "USD");

        handler.Urls.ShouldBe(["https://eodhd.com/api/eod/AAPL.US?api_token=test-key&fmt=json"]);
        price.ShouldBe(new MyWealthV2.Application.MarketData.SourcedInstrumentPrice(
            180.125m, new DateOnly(2026, 11, 3), "USD"));
    }

    [Test]
    public async Task GetLatest_UsesTheExchangeSuffix()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [{"date":"2026-11-01","close":"8.5"}]
            """);
        var client = Client(handler, "test-key");

        var price = await client.GetLatestAsync("AIA", "NZD");

        handler.Urls.ShouldBe(["https://eodhd.com/api/eod/AIA.NZ?api_token=test-key&fmt=json"]);
        price.ShouldNotBeNull();
        price.Value.AsOfDate.ShouldBe(new DateOnly(2026, 11, 1));
        price.Value.Price.ShouldBe(8.5m);
        price.Value.Currency.ShouldBe("NZD");
    }

    [Test]
    public async Task GetLatest_WhenBarHasCurrency_ReturnsThatCurrency()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [{"date":"2026-11-01","close":8.5,"currency":"USD"}]
            """);
        var client = Client(handler, "test-key");

        var price = await client.GetLatestAsync("AIA", "NZD");

        price.ShouldNotBeNull();
        price.Value.Currency.ShouldBe("USD");
    }

    [Test]
    public async Task GetLatest_UnknownSymbol_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, "Ticker Not Found.");
        var client = Client(handler, "test-key");

        (await client.GetLatestAsync("NOPE", "USD")).ShouldBeNull();

        handler.StatusCode = HttpStatusCode.Forbidden;
        (await client.GetLatestAsync("NOPE", "USD")).ShouldBeNull();

        handler.StatusCode = HttpStatusCode.OK;
        handler.Body = "[]";
        (await client.GetLatestAsync("NOPE", "USD")).ShouldBeNull();
    }

    [Test]
    public async Task GetLatest_ServerError_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, """{"error":"down"}""");
        var client = Client(handler, "test-key");

        await Should.ThrowAsync<HttpRequestException>(() => client.GetLatestAsync("AAPL", "USD"));

        handler.StatusCode = HttpStatusCode.Unauthorized;
        await Should.ThrowAsync<HttpRequestException>(() => client.GetLatestAsync("AAPL", "USD"));

        handler.StatusCode = HttpStatusCode.TooManyRequests;
        await Should.ThrowAsync<HttpRequestException>(() => client.GetLatestAsync("AAPL", "USD"));
    }

    [Test]
    public async Task GetLatest_MissingKey_ThrowsBeforeCalling()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var client = Client(handler, "");

        await Should.ThrowAsync<InvalidOperationException>(() => client.GetLatestAsync("AAPL", "USD"));
        handler.Urls.ShouldBeEmpty();
    }

    private static EodhdClient Client(StubHandler handler, string apiKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Eodhd:ApiKey"] = apiKey })
            .Build();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://eodhd.com/api") };
        return new EodhdClient(http, configuration);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string json) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        public HttpStatusCode StatusCode { get; set; } = statusCode;

        public string Body { get; set; } = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
