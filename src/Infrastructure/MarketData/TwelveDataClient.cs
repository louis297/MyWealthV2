using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.MarketData;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class TwelveDataClient(HttpClient http, IConfiguration configuration) : IInstrumentPriceSource
{
    public Task<SourcedInstrumentPrice?> GetLatestAsync(
        string symbol,
        string quoteCurrency,
        CancellationToken cancellationToken = default)
    {
        _ = http;
        _ = configuration;
        _ = symbol;
        _ = quoteCurrency;
        _ = cancellationToken;
        throw new NotImplementedException();
    }
}
