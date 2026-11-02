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
