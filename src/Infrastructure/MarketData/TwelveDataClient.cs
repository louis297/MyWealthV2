using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.MarketData;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class TwelveDataClient(HttpClient http, IConfiguration configuration) : IInstrumentPriceSource
{
    public async Task<SourcedInstrumentPrice?> GetLatestAsync(
        string symbol,
        string quoteCurrency,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["TwelveData:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Twelve Data API key is not configured.");
        }

        var providerSymbol = TwelveDataSymbols.For(symbol, quoteCurrency);
        var path =
            $"/eod?symbol={Uri.EscapeDataString(providerSymbol)}&dp=8&apikey={Uri.EscapeDataString(apiKey)}";
        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (IsError(root) || !TryReadClose(root, out var price) || !TryReadDate(root, out var asOf))
        {
            return null;
        }

        var currency = root.TryGetProperty("currency", out var currencyElement)
            && currencyElement.ValueKind == JsonValueKind.String
            && currencyElement.GetString() is { } code
            && !string.IsNullOrWhiteSpace(code)
                ? code
                : quoteCurrency;
        return new SourcedInstrumentPrice(price, asOf, currency);
    }

    private static bool IsError(JsonElement root) =>
        root.TryGetProperty("status", out var status)
        && status.ValueKind == JsonValueKind.String
        && string.Equals(status.GetString(), "error", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadClose(JsonElement root, out decimal price)
    {
        price = 0m;
        if (!root.TryGetProperty("close", out var close) || close.ValueKind is JsonValueKind.Null)
        {
            return false;
        }

        var text = close.ValueKind == JsonValueKind.String ? close.GetString() : close.GetRawText();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
    }

    private static bool TryReadDate(JsonElement root, out DateOnly asOf)
    {
        asOf = default;
        return root.TryGetProperty("datetime", out var datetime)
            && datetime.ValueKind == JsonValueKind.String
            && datetime.GetString() is { Length: >= 10 } text
            && DateOnly.TryParse(text[..10], CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf);
    }
}
