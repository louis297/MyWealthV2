using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.MarketData;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class EodhdClient(HttpClient http, IConfiguration configuration) : IInstrumentPriceSource
{
    public async Task<SourcedInstrumentPrice?> GetLatestAsync(
        string symbol,
        string quoteCurrency,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Eodhd:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("EODHD API key is not configured.");
        }

        var providerSymbol = EodhdSymbols.For(symbol, quoteCurrency);
        var path =
            $"/eod/{Uri.EscapeDataString(providerSymbol)}?api_token={Uri.EscapeDataString(apiKey)}&fmt=json";
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

            JsonElement? latest = null;
            var latestDate = default(DateOnly);
            foreach (var bar in root.EnumerateArray())
            {
                if (!TryReadDate(bar, out var asOf))
                {
                    continue;
                }

                if (latest is null || asOf > latestDate)
                {
                    latest = bar;
                    latestDate = asOf;
                }
            }

            if (latest is null || !TryReadClose(latest.Value, out var price))
            {
                return null;
            }

            var currency = quoteCurrency;
            if (latest.Value.TryGetProperty("currency", out var currencyElement)
                && currencyElement.ValueKind == JsonValueKind.String
                && currencyElement.GetString() is { } code
                && !string.IsNullOrWhiteSpace(code))
            {
                currency = code;
            }

            return new SourcedInstrumentPrice(price, latestDate, currency);
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
