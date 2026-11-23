using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>
/// The broker feed adapter for <c>Adapter = Simulator</c> (docs/features/orders.md R63, R67): calls the simulator
/// broker's report route after a cursor with the connection's key and timeout. It keeps its own wire types (ADR 0021,
/// no shared contracts). A field it cannot read comes through empty, so the pull refuses the page; an unknown kind
/// comes through as no kind. Every failure comes back as a result.
/// </summary>
public sealed class SimulatorBrokerFeed(IHttpClientFactory httpClients) : IBrokerFeedAdapter
{
    public const string HttpClientName = "broker-feed-simulator";

    private static readonly JsonSerializerOptions Json = new();

    public string Adapter => BrokerConnectionsConfiguration.SimulatorAdapter;

    public async Task<BrokerFeedResult> GetReportsAsync(
        BrokerConnectionSettings connection,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var message = new HttpRequestMessage(HttpMethod.Get, Address(connection, cursor, limit));
        message.Headers.Add(SimulatorBrokerOrders.ApiKeyHeader, connection.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return BrokerFeedResult.Failed(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => BrokerFailure.Unauthorized,
                    HttpStatusCode.ServiceUnavailable => BrokerFailure.Unavailable,
                    _ => BrokerFailure.BadResponse
                });
            }

            return Read(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BrokerFeedResult.Failed(BrokerFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return BrokerFeedResult.Failed(BrokerFailure.Unreachable);
        }
    }

    private static Uri Address(BrokerConnectionSettings connection, string? cursor, int limit)
    {
        var root = connection.BaseUrl!.ToString().TrimEnd('/');
        var size = limit.ToString(CultureInfo.InvariantCulture);
        var query = cursor is null ? $"limit={size}" : $"after={Uri.EscapeDataString(cursor)}&limit={size}";
        return new Uri($"{root}/party/broker/reports?{query}");
    }

    private static BrokerFeedResult Read(string body)
    {
        WirePage? page;
        try
        {
            page = JsonSerializer.Deserialize<WirePage>(body, Json);
        }
        catch (JsonException)
        {
            return BrokerFeedResult.Failed(BrokerFailure.BadResponse);
        }

        if (page?.Items is null || page.Cursor is { Length: > BrokerFeed.CursorMaxLength })
        {
            return BrokerFeedResult.Failed(BrokerFailure.BadResponse);
        }

        var items = page.Items.Select(item => new BrokerFeedItem(
            Text(item.Id),
            Text(item.Kind) switch
            {
                "Fill" => ExecutionReportKind.Fill,
                "Cancelled" => ExecutionReportKind.Cancelled,
                "Expired" => ExecutionReportKind.Expired,
                "Settled" => ExecutionReportKind.Settled,
                "Settlement" => ExecutionReportKind.Settlement,
                "Broken" => ExecutionReportKind.Broken,
                _ => null
            },
            Text(item.ClientOrderId),
            Text(item.BrokerOrderId),
            Number(item.Quantity),
            Number(item.Price),
            Date(item.TradeDate),
            Date(item.SettlementDate),
            Text(item.ContractNote),
            Text(item.FillId),
            Date(item.SettledDate),
            Text(item.Reason),
            SettlementOf(item))).ToList();
        return BrokerFeedResult.Succeeded(new BrokerFeedPage(items, page.Cursor, page.HasMore ?? false));
    }

    /// <summary>Trade-settlement R41, R43: the five settlement fields, or nothing when the item carries none. A net
    /// that is not a JSON number or a count that is not a whole number comes through empty.</summary>
    private static ExecutionReportSettlement? SettlementOf(WireItem item)
    {
        var settlement = new ExecutionReportSettlement(
            Text(item.SettlementId), Text(item.SettlementAccount), Text(item.Currency), Number(item.NetAmount), WholeNumber(item.FillCount));
        return settlement == new ExecutionReportSettlement(null) ? null : settlement;
    }

    private static int? WholeNumber(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var whole) ? whole : null;

    /// <summary>A JSON string, or nothing: any other shape comes through empty.</summary>
    private static string? Text(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    /// <summary>R67: a quantity or price that is not a JSON number comes through empty.</summary>
    private static decimal? Number(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number && number.TryGetDecimal(out var amount) ? amount : null;

    /// <summary>R67: a date that is not <c>yyyy-MM-dd</c> comes through empty.</summary>
    private static DateOnly? Date(JsonElement? value) =>
        DateOnly.TryParseExact(Text(value), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private sealed record WirePage(
        [property: JsonPropertyName("items")] List<WireItem>? Items,
        [property: JsonPropertyName("cursor")] string? Cursor,
        [property: JsonPropertyName("hasMore")] bool? HasMore);

    private sealed record WireItem(
        [property: JsonPropertyName("id")] JsonElement? Id,
        [property: JsonPropertyName("kind")] JsonElement? Kind,
        [property: JsonPropertyName("clientOrderId")] JsonElement? ClientOrderId,
        [property: JsonPropertyName("brokerOrderId")] JsonElement? BrokerOrderId,
        [property: JsonPropertyName("quantity")] JsonElement? Quantity,
        [property: JsonPropertyName("price")] JsonElement? Price,
        [property: JsonPropertyName("tradeDate")] JsonElement? TradeDate,
        [property: JsonPropertyName("settlementDate")] JsonElement? SettlementDate,
        [property: JsonPropertyName("contractNote")] JsonElement? ContractNote,
        [property: JsonPropertyName("fillId")] JsonElement? FillId,
        [property: JsonPropertyName("settledDate")] JsonElement? SettledDate,
        [property: JsonPropertyName("reason")] JsonElement? Reason,
        [property: JsonPropertyName("settlementId")] JsonElement? SettlementId = null,
        [property: JsonPropertyName("settlementAccount")] JsonElement? SettlementAccount = null,
        [property: JsonPropertyName("currency")] JsonElement? Currency = null,
        [property: JsonPropertyName("netAmount")] JsonElement? NetAmount = null,
        [property: JsonPropertyName("fillCount")] JsonElement? FillCount = null);
}
