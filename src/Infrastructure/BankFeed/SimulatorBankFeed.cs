using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>
/// The bank feed adapter for <c>Adapter = Simulator</c> (deposit-matching R49): calls the simulator bank's party
/// route (R44) with the connection's key and timeout. It keeps its own wire types (ADR 0021, no shared contracts).
/// Every failure comes back as a result.
/// </summary>
public sealed class SimulatorBankFeed(IHttpClientFactory httpClients) : IBankFeedAdapter
{
    public const string HttpClientName = "bank-feed-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    // Numbers must be JSON numbers (R45); the web defaults would also read "500.00".
    private static readonly JsonSerializerOptions Json = new();

    public string Adapter => BankConnectionsConfiguration.SimulatorAdapter;

    public async Task<BankFeedResult> GetStatementLinesAsync(
        BankConnectionSettings connection,
        BankFeedRequest request,
        CancellationToken cancellationToken)
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
                return BankFeedResult.Failed(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => BankFeedFailure.Unauthorized,
                    HttpStatusCode.ServiceUnavailable => BankFeedFailure.Unavailable,
                    HttpStatusCode.NotFound => BankFeedFailure.UnknownAccount,
                    _ => BankFeedFailure.BadResponse
                });
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Read(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BankFeedResult.Failed(BankFeedFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return BankFeedResult.Failed(BankFeedFailure.Unreachable);
        }
    }

    private static Uri Address(BankConnectionSettings connection, BankFeedRequest request)
    {
        var root = connection.BaseUrl!.ToString().TrimEnd('/');
        var query = request.Cursor is null
            ? $"limit={request.Limit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={Uri.EscapeDataString(request.Cursor)}&limit={request.Limit.ToString(CultureInfo.InvariantCulture)}";
        return new Uri(
            $"{root}/party/bank/accounts/{Uri.EscapeDataString(request.AccountNumber)}/statement-lines?{query}");
    }

    private static BankFeedResult Read(string body)
    {
        WirePage? page;
        try
        {
            page = JsonSerializer.Deserialize<WirePage>(body, Json);
        }
        catch (JsonException)
        {
            return BankFeedResult.Failed(BankFeedFailure.BadResponse);
        }

        // Bank-reconciliation R31: a balance is a JSON number with at most 4 decimal places, or absent.
        if (page?.Items is null || page.Balance is decimal balance && decimal.Round(balance, 4) != balance)
        {
            return BankFeedResult.Failed(BankFeedFailure.BadResponse);
        }

        var lines = page.Items
            .Select(item => new BankFeedLine(
                item.Id,
                DateOnly.TryParseExact(item.ValueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null,
                item.Direction switch
                {
                    "Credit" => BankLineDirection.Credit,
                    "Debit" => BankLineDirection.Debit,
                    _ => null
                },
                item.Amount,
                item.Currency,
                item.PayerName,
                item.PayerAccount,
                item.Reference,
                item.EndToEndId,
                item.ReturnOf,
                item.ReturnReason))
            .ToList();
        return BankFeedResult.Succeeded(new BankFeedPage(lines, page.Cursor, page.HasMore, page.Balance));
    }

    private sealed record WirePage(
        [property: JsonPropertyName("items")] List<WireLine>? Items,
        [property: JsonPropertyName("cursor")] string? Cursor,
        [property: JsonPropertyName("hasMore")] bool HasMore,
        [property: JsonPropertyName("balance")] decimal? Balance = null);

    private sealed record WireLine(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("valueDate")] string? ValueDate,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("amount")] decimal? Amount,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("payerName")] string? PayerName,
        [property: JsonPropertyName("payerAccount")] string? PayerAccount,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("endToEndId")] string? EndToEndId = null,
        [property: JsonPropertyName("returnOf")] string? ReturnOf = null,
        [property: JsonPropertyName("returnReason")] string? ReturnReason = null);
}
