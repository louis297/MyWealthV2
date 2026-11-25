using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.MarketData;
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
    public async Task GetBars_WithoutFrom_RequestsEodAndReturnsTheLatestBarOnly()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [
              {"date":"2026-11-01","close":100.0},
              {"date":"2026-11-03","close":180.125},
              {"date":"2026-11-02","close":170.0}
            ]
            """);
        var client = Client(handler, "test-key");

        var bars = await client.GetBarsAsync("AAPL", "USD");

        handler.Urls.ShouldBe(["https://eodhd.com/api/eod/AAPL.US?api_token=test-key&fmt=json"]);
        bars.ShouldNotBeNull();
        bars.Currency.ShouldBe("USD");
        bars.Bars.ShouldBe([new SourcedPriceBar(new DateOnly(2026, 11, 3), 180.125m)]);
    }

    /// <summary>Market data history §1.1 and R7: the stored answer for AAPL.US around its 4-for-1 split of
    /// 2020-08-31. Every bar is returned with its unadjusted <c>close</c>, never <c>adjusted_close</c>.</summary>
    [Test]
    public async Task GetBars_WithFrom_SendsFromAndReturnsEveryBarWithTheUnadjustedClose()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [
              {"date":"2020-08-27","open":508.57,"high":509.94,"low":495.33,"close":500.04,"adjusted_close":121.0599,"volume":38888096},
              {"date":"2020-08-28","open":504.05,"high":505.77,"low":498.31,"close":499.23,"adjusted_close":120.8638,"volume":46907479},
              {"date":"2020-08-31","open":127.58,"high":131,"low":126,"close":129.04,"adjusted_close":124.9626,"volume":225702688},
              {"date":"2020-09-01","open":132.76,"high":134.8,"low":130.53,"close":134.18,"adjusted_close":129.9402,"volume":152470128}
            ]
            """);
        var client = Client(handler, "test-key");

        var bars = await client.GetBarsAsync("AAPL", "USD", new DateOnly(2020, 8, 27));

        handler.Urls.ShouldBe(["https://eodhd.com/api/eod/AAPL.US?api_token=test-key&fmt=json&from=2020-08-27"]);
        bars.ShouldNotBeNull();
        bars.Currency.ShouldBe("USD");
        bars.Bars.ShouldBe([
            new SourcedPriceBar(new DateOnly(2020, 8, 27), 500.04m),
            new SourcedPriceBar(new DateOnly(2020, 8, 28), 499.23m),
            new SourcedPriceBar(new DateOnly(2020, 8, 31), 129.04m),
            new SourcedPriceBar(new DateOnly(2020, 9, 1), 134.18m)
        ]);
    }

    [Test]
    public async Task GetBars_WithFrom_SkipsABarWithoutADate_AndReturnsABarWithoutACloseAsNoClose()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [
              {"close":1.0},
              {"date":"2026-01-02","close":null},
              {"date":"2026-01-05","adjusted_close":267.0},
              {"date":"2026-01-06","close":"268.5"}
            ]
            """);
        var client = Client(handler, "test-key");

        var bars = await client.GetBarsAsync("AAPL", "USD", new DateOnly(2026, 1, 1));

        bars.ShouldNotBeNull();
        bars.Bars.ShouldBe([
            new SourcedPriceBar(new DateOnly(2026, 1, 2), null),
            new SourcedPriceBar(new DateOnly(2026, 1, 5), null),
            new SourcedPriceBar(new DateOnly(2026, 1, 6), 268.5m)
        ]);
    }

    [Test]
    public async Task GetBars_UsesTheExchangeSuffix()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [{"date":"2026-11-01","close":"8.5"}]
            """);
        var client = Client(handler, "test-key");

        var bars = await client.GetBarsAsync("AIA", "NZD");

        handler.Urls.ShouldBe(["https://eodhd.com/api/eod/AIA.NZ?api_token=test-key&fmt=json"]);
        bars.ShouldNotBeNull();
        bars.Currency.ShouldBe("NZD");
        bars.Bars.ShouldBe([new SourcedPriceBar(new DateOnly(2026, 11, 1), 8.5m)]);
    }

    [Test]
    public async Task GetBars_WhenBarHasCurrency_ReturnsThatCurrency()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [{"date":"2026-11-01","close":8.5,"currency":"USD"}]
            """);
        var client = Client(handler, "test-key");

        var bars = await client.GetBarsAsync("AIA", "NZD");

        bars.ShouldNotBeNull();
        bars.Currency.ShouldBe("USD");
    }

    [Test]
    public async Task GetBars_UnknownSymbolOrEmptyArray_IsARejection()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, "Ticker Not Found.");
        var client = Client(handler, "test-key");

        (await client.GetBarsAsync("NOPE", "USD")).ShouldBeNull();
        (await client.GetBarsAsync("NOPE", "USD", new DateOnly(2026, 1, 1))).ShouldBeNull();

        handler.StatusCode = HttpStatusCode.Forbidden;
        (await client.GetBarsAsync("NOPE", "USD")).ShouldBeNull();

        handler.StatusCode = HttpStatusCode.OK;
        handler.Body = "[]";
        (await client.GetBarsAsync("NOPE", "USD")).ShouldBeNull();
        (await client.GetBarsAsync("NOPE", "USD", new DateOnly(2026, 1, 1))).ShouldBeNull();
    }

    [Test]
    public async Task GetBars_ServerError_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, """{"error":"down"}""");
        var client = Client(handler, "test-key");

        await Should.ThrowAsync<HttpRequestException>(() => client.GetBarsAsync("AAPL", "USD"));

        handler.StatusCode = HttpStatusCode.Unauthorized;
        await Should.ThrowAsync<HttpRequestException>(() => client.GetBarsAsync("AAPL", "USD"));

        handler.StatusCode = HttpStatusCode.TooManyRequests;
        await Should.ThrowAsync<HttpRequestException>(() => client.GetBarsAsync("AAPL", "USD", new DateOnly(2026, 1, 1)));
    }

    [Test]
    public async Task GetBars_MissingKey_ThrowsBeforeCalling()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var client = Client(handler, "");

        await Should.ThrowAsync<InvalidOperationException>(() => client.GetBarsAsync("AAPL", "USD"));
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
