using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>
/// The broker orders adapter for <c>Adapter = Simulator</c> (docs/features/orders.md R53, R59, R66): places one order
/// and asks to cancel one order at the simulator broker's party routes, with the connection's key and timeout. It
/// keeps its own wire types (ADR 0021, no shared contracts). Every failure comes back as a result; a 400 or 409 is a
/// bad response, since the place body is built from the stored order and is the same on every attempt.
/// </summary>
public sealed class SimulatorBrokerOrders(IHttpClientFactory httpClients) : IBrokerOrdersAdapter
{
    public const string HttpClientName = "broker-orders-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    private static readonly JsonSerializerOptions Json = new();

    public string Adapter => BrokerConnectionsConfiguration.SimulatorAdapter;

    public async Task<BrokerPlaceResult> PlaceAsync(
        BrokerConnectionSettings connection,
        BrokerOrderRequest order,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Root(connection)}/party/broker/orders");
        message.Content = JsonContent.Create(
            new WireOrder(
                order.ClientOrderId, order.Symbol, order.Currency, order.Side.ToString(), order.Quantity, order.Type.ToString(),
                order.TimeInForce.ToString(), order.PriceLimit, order.ReferencePrice, order.SettlementAccount,
                order.SafekeepingAccount),
            options: Json);
        var (status, body, failure) = await SendAsync(connection, message, cancellationToken);
        if (failure is not null)
        {
            return BrokerPlaceResult.Failed(failure.Value);
        }

        if (status != HttpStatusCode.OK)
        {
            return BrokerPlaceResult.Failed(FailureOf(status));
        }

        var answer = Read(body);
        return answer switch
        {
            { Status: "Accepted", BrokerOrderId: { Length: > 0 and <= 100 } brokerOrderId } => BrokerPlaceResult.Accepted(brokerOrderId),
            // R21: a reason the adapter does not know is Refused.
            { Status: "Rejected" } => BrokerPlaceResult.Rejected(
                Enum.TryParse<OrderRejectReason>(answer.Reason, ignoreCase: false, out var reason) && Enum.IsDefined(reason)
                && !IsNumeric(answer.Reason)
                    ? reason
                    : OrderRejectReason.Refused),
            _ => BrokerPlaceResult.Failed(BrokerFailure.BadResponse)
        };
    }

    public async Task<BrokerCancelResult> CancelAsync(
        BrokerConnectionSettings connection,
        string clientOrderId,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"{Root(connection)}/party/broker/orders/{Uri.EscapeDataString(clientOrderId)}/cancel");
        var (status, body, failure) = await SendAsync(connection, message, cancellationToken);
        if (failure is not null)
        {
            return BrokerCancelResult.Failed(failure.Value);
        }

        // R66: a 404 is an answer, not a failure: the broker never had the order.
        if (status == HttpStatusCode.NotFound)
        {
            return BrokerCancelResult.Answered(BrokerCancelAnswer.UnknownOrder);
        }

        if (status != HttpStatusCode.OK)
        {
            return BrokerCancelResult.Failed(FailureOf(status));
        }

        return Read(body)?.Status switch
        {
            "Accepted" => BrokerCancelResult.Answered(BrokerCancelAnswer.Accepted),
            "TooLate" => BrokerCancelResult.Answered(BrokerCancelAnswer.TooLate),
            _ => BrokerCancelResult.Failed(BrokerFailure.BadResponse)
        };
    }

    private async Task<(HttpStatusCode Status, string Body, BrokerFailure? Failure)> SendAsync(
        BrokerConnectionSettings connection,
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        message.Headers.Add(ApiKeyHeader, connection.ApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync(timeout.Token) : string.Empty;
            return (response.StatusCode, body, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (default, string.Empty, BrokerFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return (default, string.Empty, BrokerFailure.Unreachable);
        }
    }

    private static BrokerFailure FailureOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => BrokerFailure.Unauthorized,
        HttpStatusCode.ServiceUnavailable => BrokerFailure.Unavailable,
        _ => BrokerFailure.BadResponse
    };

    private static WireAnswer? Read(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<WireAnswer>(body, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsNumeric(string? value) => value is not null && value.Length > 0 && value.All(char.IsAsciiDigit);

    private static string Root(BrokerConnectionSettings connection) => connection.BaseUrl!.ToString().TrimEnd('/');

    private sealed record WireOrder(
        [property: JsonPropertyName("clientOrderId")] string ClientOrderId,
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("side")] string Side,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("timeInForce")] string TimeInForce,
        [property: JsonPropertyName("priceLimit")] decimal PriceLimit,
        [property: JsonPropertyName("referencePrice")] decimal ReferencePrice,
        [property: JsonPropertyName("settlementAccount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? SettlementAccount,
        [property: JsonPropertyName("safekeepingAccount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? SafekeepingAccount);

    private sealed record WireAnswer(
        [property: JsonPropertyName("brokerOrderId")] string? BrokerOrderId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("reason")] string? Reason);
}
