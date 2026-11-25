using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.MarketData;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>
/// The EODHD end-of-day source (docs/features/instrument-prices.md, market-data-history.md R7, R8). One call per
/// symbol. With <c>from</c> it returns every bar of the answer; without it, the latest bar only. The price is the
/// unadjusted <c>close</c>; <c>adjusted_close</c> is never read.
/// </summary>
public sealed class EodhdClient(HttpClient http, IConfiguration configuration) : IInstrumentPriceSource
{
    public async Task<SourcedInstrumentBars?> GetBarsAsync(
        string symbol,
        string quoteCurrency,
        DateOnly? from = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Eodhd:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("EODHD API key is not configured.");
        }

        var providerSymbol = EodhdSymbols.For(symbol, quoteCurrency);
        // A request path that starts with '/' replaces BaseAddress's path, so /api must be in the path.
        var path =
            $"/api/eod/{Uri.EscapeDataString(providerSymbol)}?api_token={Uri.EscapeDataString(apiKey)}&fmt=json";
        if (from is { } first)
        {
            path += $"&from={first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        }

        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var bars = new List<(SourcedPriceBar Bar, JsonElement Element)>();
            foreach (var element in root.EnumerateArray())
            {
                if (!TryReadDate(element, out var asOf))
                {
                    continue;
                }

                bars.Add((new SourcedPriceBar(asOf, TryReadClose(element, out var close) ? close : null), element));
            }

            if (bars.Count == 0)
            {
                return null;
            }

            var latest = bars.MaxBy(entry => entry.Bar.AsOfDate);
            var currency = quoteCurrency;
            if (latest.Element.TryGetProperty("currency", out var currencyElement)
                && currencyElement.ValueKind == JsonValueKind.String
                && currencyElement.GetString() is { } code
                && !string.IsNullOrWhiteSpace(code))
            {
                currency = code;
            }

            var result = from is null
                ? [latest.Bar]
                : bars.Select(entry => entry.Bar).OrderBy(bar => bar.AsOfDate).ToList();
            return new SourcedInstrumentBars(currency, result);
        }
    }

    private static bool TryReadClose(JsonElement bar, out decimal price)
    {
        price = 0m;
        if (!bar.TryGetProperty("close", out var close) || close.ValueKind is JsonValueKind.Null)
        {
            return false;
        }

        var text = close.ValueKind == JsonValueKind.String ? close.GetString() : close.GetRawText();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
    }

    private static bool TryReadDate(JsonElement bar, out DateOnly asOf)
    {
        asOf = default;
        return bar.TryGetProperty("date", out var date)
            && date.ValueKind == JsonValueKind.String
            && date.GetString() is { Length: >= 10 } text
            && DateOnly.TryParse(text[..10], CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf);
    }
}
