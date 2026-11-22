using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>
/// The corporate action feed adapter for <c>Adapter = Simulator</c> (docs/features/corporate-actions.md R48): calls the
/// simulator market's feed route (R44) after a cursor with the connection's key and timeout. It keeps its own wire
/// types (ADR 0021, no shared contracts). A field it cannot read comes through empty, so the pull refuses the page;
/// an unknown kind comes through as no kind. Every failure comes back as a result.
/// </summary>
public sealed class SimulatorCorporateActionFeed(IHttpClientFactory httpClients) : ICorporateActionFeedAdapter
{
    public const string HttpClientName = "corporate-action-feed-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    public string Adapter => MarketDataConnectionsConfiguration.SimulatorAdapter;

    public async Task<CorporateActionFeedResult> GetAnnouncementsAsync(
        MarketDataConnectionSettings connection,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var message = new HttpRequestMessage(HttpMethod.Get, Address(connection, cursor, limit));
        message.Headers.Add(ApiKeyHeader, connection.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return CorporateActionFeedResult.Failed(response.StatusCode switch
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
            return CorporateActionFeedResult.Failed(MarketDataFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return CorporateActionFeedResult.Failed(MarketDataFailure.Unreachable);
        }
    }

    private static Uri Address(MarketDataConnectionSettings connection, string? cursor, int limit)
    {
        var root = connection.BaseUrl!.ToString().TrimEnd('/');
        var size = limit.ToString(CultureInfo.InvariantCulture);
        var query = cursor is null ? $"limit={size}" : $"after={Uri.EscapeDataString(cursor)}&limit={size}";
        return new Uri($"{root}/party/market/corporate-actions?{query}");
    }

    private static CorporateActionFeedResult Read(string body)
    {
        WirePage? page;
        try
        {
            page = JsonSerializer.Deserialize<WirePage>(body, Json);
        }
        catch (JsonException)
        {
            return CorporateActionFeedResult.Failed(MarketDataFailure.BadResponse);
        }

        if (page?.Items is null || page.Cursor is { Length: > MarketDataFeed.CursorMaxLength })
        {
            return CorporateActionFeedResult.Failed(MarketDataFailure.BadResponse);
        }

        var items = page.Items.Select(item => new CorporateActionFeedItem(
            item.Id,
            item.ActionId,
            WholeNumber(item.Version),
            Enum.TryParse<PartyActionStatus>(item.Status, ignoreCase: false, out var status) && Enum.IsDefined(status) && !IsNumeric(item.Status)
                ? status
                : null,
            item.Symbol,
            item.Currency,
            item.Kind switch
            {
                "Split" => CorporateActionKind.Split,
                "Consolidation" => CorporateActionKind.Consolidation,
                "Bonus" => CorporateActionKind.Bonus,
                _ => null
            },
            WholeNumber(item.RatioNew),
            WholeNumber(item.RatioHeld),
            Date(item.ExDate),
            Date(item.RecordDate),
            Date(item.PaymentDate))).ToList();
        return CorporateActionFeedResult.Succeeded(new CorporateActionFeedPage(items, page.Cursor, page.HasMore ?? false));
    }

    private static bool IsNumeric(string? value) => value is not null && value.Length > 0 && value.All(char.IsAsciiDigit);

    /// <summary>R48: a ratio or version that is not a whole JSON number comes through empty.</summary>
    private static int? WholeNumber(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var whole) ? whole : null;

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static readonly JsonSerializerOptions Json = new();

    private sealed record WirePage(
        [property: JsonPropertyName("items")] List<WireItem>? Items,
        [property: JsonPropertyName("cursor")] string? Cursor,
        [property: JsonPropertyName("hasMore")] bool? HasMore);

    private sealed record WireItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("actionId")] string? ActionId,
        [property: JsonPropertyName("version")] JsonElement? Version,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("ratioNew")] JsonElement? RatioNew,
        [property: JsonPropertyName("ratioHeld")] JsonElement? RatioHeld,
        [property: JsonPropertyName("exDate")] string? ExDate,
        [property: JsonPropertyName("recordDate")] string? RecordDate,
        [property: JsonPropertyName("paymentDate")] string? PaymentDate);
}
