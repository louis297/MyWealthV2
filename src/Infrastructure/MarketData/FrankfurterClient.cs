using System.Globalization;
using System.Net;
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

    /// <summary>Market data history R13: <c>GET /v2/rates?base=&amp;quotes=&amp;from=&amp;to=</c>, one record per
    /// date and quote. Any status other than 200, or a body that cannot be read, throws.</summary>
    public async Task<IReadOnlyList<FxRateQuote>> GetRangeAsync(
        string baseCurrency,
        IReadOnlyList<string> quotes,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var symbols = string.Join(',', quotes.Select(code => Uri.EscapeDataString(code.Trim())));
        var path =
            $"/v2/rates?base={Uri.EscapeDataString(baseCurrency.Trim())}&quotes={symbols}"
            + $"&from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"Frankfurter answered {(int)response.StatusCode} for base {baseCurrency}.", null, response.StatusCode);
        }

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
