using System.Net;
using System.Text;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Fx;

public class FrankfurterClientTests
{
    [Test]
    public async Task GetLatest_RequestsOneBaseAndReadsTheRateArray()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev") };
        var client = new FrankfurterClient(http);

        var rows = await client.GetLatestAsync("USD", ["NZD", "AUD"]);

        handler.Urls.ShouldBe(["/v2/rates?base=USD&quotes=NZD,AUD"]);
        rows.ShouldBe([
            new MyWealthV2.Application.Fx.FxRateQuote("USD", "NZD", 1.7753m, new DateOnly(2026, 11, 2)),
            new MyWealthV2.Application.Fx.FxRateQuote("USD", "AUD", 1.4231m, new DateOnly(2026, 11, 2))
        ]);
    }

    /// <summary>Market data history §1.1, R13: the stored Frankfurter v2 answer for USD over New Year 2026. A rate is
    /// returned for every calendar day, the weekend included.</summary>
    [Test]
    public async Task GetRange_SendsFromAndTo_AndReturnsEveryDayWithTheWeekend()
    {
        var handler = new RangeHandler(HttpStatusCode.OK, """
            [
              {"date":"2026-01-01","base":"USD","quote":"NZD","rate":1.7294},
              {"date":"2026-01-01","base":"USD","quote":"AUD","rate":1.4954},
              {"date":"2026-01-02","base":"USD","quote":"NZD","rate":1.7313},
              {"date":"2026-01-02","base":"USD","quote":"AUD","rate":1.4952},
              {"date":"2026-01-03","base":"USD","quote":"NZD","rate":1.7315},
              {"date":"2026-01-03","base":"USD","quote":"AUD","rate":1.4951},
              {"date":"2026-01-04","base":"USD","quote":"NZD","rate":1.7320},
              {"date":"2026-01-04","base":"USD","quote":"AUD","rate":1.4951},
              {"date":"2026-01-05","base":"USD","quote":"NZD","rate":1.7350},
              {"date":"2026-01-05","base":"USD","quote":"AUD","rate":1.4966}
            ]
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev") };
        var client = new FrankfurterClient(http);

        var rows = await client.GetRangeAsync("USD", ["NZD", "AUD"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        handler.Urls.ShouldBe(["/v2/rates?base=USD&quotes=NZD,AUD&from=2026-01-01&to=2026-01-05"]);
        rows.Count.ShouldBe(10);
        rows.Where(row => row.QuoteCurrency == "NZD").Select(row => row.AsOfDate).ShouldBe([
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 5)
        ]);
        rows.ShouldContain(new MyWealthV2.Application.Fx.FxRateQuote("USD", "NZD", 1.7320m, new DateOnly(2026, 1, 4)));
    }

    [Test]
    public async Task GetRange_AStatusOtherThan200OrABodyThatCannotBeRead_Throws()
    {
        var handler = new RangeHandler(HttpStatusCode.InternalServerError, "{}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev") };
        var client = new FrankfurterClient(http);
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 5);

        await Should.ThrowAsync<HttpRequestException>(() => client.GetRangeAsync("USD", ["NZD"], from, to));

        handler.StatusCode = HttpStatusCode.OK;
        handler.Body = "not json";
        await Should.ThrowAsync<System.Text.Json.JsonException>(() => client.GetRangeAsync("USD", ["NZD"], from, to));
    }

    private sealed class RangeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        public HttpStatusCode StatusCode { get; set; } = statusCode;

        public string Body { get; set; } = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.PathAndQuery);
            var json = """
                [
                  {"date":"2026-11-02","base":"USD","quote":"NZD","rate":1.7753},
                  {"date":"2026-11-02","base":"USD","quote":"AUD","rate":1.4231}
                ]
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
