using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Fx;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class FrankfurterClient(HttpClient http) : IFxRateSource
{
    public async Task<IReadOnlyList<FxRateQuote>> GetLatestAsync(
        string baseCurrency,
        IReadOnlyList<string> quotes,
        CancellationToken cancellationToken = default)
    {
        var symbols = string.Join(',', quotes.Select(code => Uri.EscapeDataString(code.Trim())));
        var path =
            $"/v2/rates?base={Uri.EscapeDataString(baseCurrency.Trim())}&quotes={symbols}";
        using var response = await http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<FrankfurterRate>>(cancellationToken) ?? [];
        return rows
            .Select(row => new FxRateQuote(row.Base, row.Quote, row.Rate, row.Date))
            .ToList();
    }

    private sealed record FrankfurterRate(
        [property: JsonPropertyName("date")] DateOnly Date,
        [property: JsonPropertyName("base")] string Base,
        [property: JsonPropertyName("quote")] string Quote,
        [property: JsonPropertyName("rate")] decimal Rate);
}
