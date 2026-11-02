using MyWealthV2.Application.Fx;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class FrankfurterClient(HttpClient http) : IFxRateSource
{
    public Task<IReadOnlyList<FxRateQuote>> GetLatestAsync(
        string baseCurrency,
        IReadOnlyList<string> quotes,
        CancellationToken cancellationToken = default)
    {
        _ = http;
        _ = baseCurrency;
        _ = quotes;
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<FxRateQuote>>([]);
    }
}
