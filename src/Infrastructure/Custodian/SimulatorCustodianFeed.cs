using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>
/// The custodian feed adapter for <c>Adapter = Simulator</c> (in-kind-transfers R73): calls the simulator custodian's
/// party route (R69) with the connection's key and timeout. It keeps its own wire types (ADR 0021, no shared contracts).
/// Every failure comes back as a result.
/// </summary>
public sealed class SimulatorCustodianFeed(IHttpClientFactory httpClients) : ICustodianFeedAdapter
{
    public const string HttpClientName = "custodian-feed-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    // Quantities must be JSON numbers (R69); the web defaults would also read "100".
    private static readonly JsonSerializerOptions Json = new();

    public string Adapter => CustodianConnectionsConfiguration.SimulatorAdapter;

    public async Task<CustodianFeedResult> GetMovementsAsync(
        CustodianConnectionSettings connection, CustodianFeedRequest request, CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var message = new HttpRequestMessage(HttpMethod.Get, Address(connection, request));
        message.Headers.Add(ApiKeyHeader, connection.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return CustodianFeedResult.Failed(Failure(response.StatusCode));
            }

            return Read(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CustodianFeedResult.Failed(CustodianFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return CustodianFeedResult.Failed(CustodianFailure.Unreachable);
        }
    }

    internal static CustodianFailure Failure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => CustodianFailure.Unauthorized,
        HttpStatusCode.ServiceUnavailable => CustodianFailure.Unavailable,
        HttpStatusCode.NotFound => CustodianFailure.UnknownAccount,
        _ => CustodianFailure.BadResponse
    };

    private static Uri Address(CustodianConnectionSettings connection, CustodianFeedRequest request)
    {
        var root = connection.BaseUrl!.ToString().TrimEnd('/');
        var limit = request.Limit.ToString(CultureInfo.InvariantCulture);
        var query = request.Cursor is null ? $"limit={limit}" : $"after={Uri.EscapeDataString(request.Cursor)}&limit={limit}";
        return new Uri($"{root}/party/custodian/accounts/{Uri.EscapeDataString(request.AccountNumber)}/movements?{query}");
    }

    private static CustodianFeedResult Read(string body)
    {
        WirePage? page;
        try
        {
            page = JsonSerializer.Deserialize<WirePage>(body, Json);
        }
        catch (JsonException)
        {
            return CustodianFeedResult.Failed(CustodianFailure.BadResponse);
        }

        if (page?.Items is null || page.Cursor is { Length: > CustodianFeedCursor.CursorMaxLength })
        {
            return CustodianFeedResult.Failed(CustodianFailure.BadResponse);
        }

        var movements = page.Items
            .Select(item => new CustodianFeedMovement(
                item.Id,
                item.Direction switch
                {
                    "In" => CustodyMovementDirection.In,
                    "Out" => CustodyMovementDirection.Out,
                    _ => null
                },
                item.Kind switch
                {
                    "Transfer" => CustodyMovementKind.Transfer,
                    "Trade" => CustodyMovementKind.Trade,
                    "CorporateAction" => CustodyMovementKind.CorporateAction,
                    _ => null
                },
                item.Symbol,
                item.Currency,
                Number(item.Quantity),
                DateOnly.TryParseExact(item.SettlementDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null,
                item.InstructionId,
                item.TradeReference,
                item.CorporateActionId,
                item.CounterpartyName,
                item.CounterpartyAccount,
                item.Reference))
            .ToList();
        var positions = page.Positions?.Select(item => new CustodianFeedPosition(item.Symbol, item.Currency, Number(item.Quantity))).ToList();
        return CustodianFeedResult.Succeeded(new CustodianFeedPage(movements, page.Cursor, page.HasMore, positions));
    }

    /// <summary>R73: a quantity that is not a JSON number comes through empty, so the pull refuses the page.</summary>
    private static decimal? Number(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number && number.TryGetDecimal(out var parsed) ? parsed : null;

    private sealed record WirePage(
        [property: JsonPropertyName("items")] List<WireMovement>? Items,
        [property: JsonPropertyName("cursor")] string? Cursor,
        [property: JsonPropertyName("hasMore")] bool HasMore,
        [property: JsonPropertyName("positions")] List<WirePosition>? Positions = null);

    private sealed record WireMovement(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("quantity")] JsonElement? Quantity,
        [property: JsonPropertyName("settlementDate")] string? SettlementDate,
        [property: JsonPropertyName("instructionId")] string? InstructionId = null,
        [property: JsonPropertyName("tradeReference")] string? TradeReference = null,
        [property: JsonPropertyName("corporateActionId")] string? CorporateActionId = null,
        [property: JsonPropertyName("counterpartyName")] string? CounterpartyName = null,
        [property: JsonPropertyName("counterpartyAccount")] string? CounterpartyAccount = null,
        [property: JsonPropertyName("reference")] string? Reference = null);

    private sealed record WirePosition(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("quantity")] JsonElement? Quantity);
}
