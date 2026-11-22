using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>
/// The market prices adapter for <c>Adapter = Simulator</c> (docs/features/simulated-instruments.md R25): calls the
/// simulator market's price route (R21) with the connection's key and timeout. It keeps its own wire types (ADR 0021,
/// no shared contracts). Every failure comes back as a result.
/// </summary>
public sealed class SimulatorMarketPrices(IHttpClientFactory httpClients) : IMarketPricesAdapter
{
    public const string HttpClientName = "market-prices-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    public string Adapter => MarketDataConnectionsConfiguration.SimulatorAdapter;

    public async Task<MarketPricesResult> GetPricesAsync(
        MarketDataConnectionSettings connection,
        CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        var root = connection.BaseUrl!.ToString().TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri($"{root}/party/market/prices"));
        message.Headers.Add(ApiKeyHeader, connection.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return MarketPricesResult.Failed(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => MarketDataFailure.Unauthorized,
                    HttpStatusCode.ServiceUnavailable => MarketDataFailure.Unavailable,
                    _ => MarketDataFailure.BadResponse
                });
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Read(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return MarketPricesResult.Failed(MarketDataFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return MarketPricesResult.Failed(MarketDataFailure.Unreachable);
        }
    }

    private static MarketPricesResult Read(string body)
    {
        WireAnswer? answer;
        try
        {
            // Plain options: a price must be a JSON number (R25); the web defaults would also read "101.37".
            answer = JsonSerializer.Deserialize<WireAnswer>(body, Json);
        }
        catch (JsonException)
        {
            return MarketPricesResult.Failed(MarketDataFailure.BadResponse);
        }

        if (answer?.Items is null)
        {
            return MarketPricesResult.Failed(MarketDataFailure.BadResponse);
        }

        var items = answer.Items
            .Select(item => new MarketPriceItem(
                item.Symbol,
                item.Currency,
                item.Price,
                DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null))
            .ToList();
        return MarketPricesResult.Succeeded(items);
    }

    private static readonly JsonSerializerOptions Json = new();

    private sealed record WireAnswer(
        [property: JsonPropertyName("asOf")] DateTimeOffset? AsOf,
        [property: JsonPropertyName("items")] List<WireItem>? Items);

    private sealed record WireItem(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("price")] decimal? Price,
        [property: JsonPropertyName("date")] string? Date);
}
