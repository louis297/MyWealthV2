using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>
/// The bank payments adapter for <c>Adapter = Simulator</c> (withdrawals R49–R51, R58): posts one payment to the
/// simulator bank's party route with the connection's key and timeout. It keeps its own wire types (ADR 0021, no
/// shared contracts). Every failure comes back as a result; a 400 or 409 is a bad response, since the body is built
/// from stored data and is the same on every attempt.
/// </summary>
public sealed class SimulatorBankPayments(IHttpClientFactory httpClients) : IBankPaymentsAdapter
{
    public const string HttpClientName = "bank-payments-simulator";

    private static readonly JsonSerializerOptions Json = new();

    public string Adapter => BankConnectionsConfiguration.SimulatorAdapter;

    public async Task<BankPaymentResult> SendAsync(
        BankConnectionSettings connection,
        BankPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var message = new HttpRequestMessage(HttpMethod.Post, Address(connection, request));
        message.Headers.Add(SimulatorBankFeed.ApiKeyHeader, connection.ApiKey);
        message.Content = JsonContent.Create(new WirePayment(
            request.InstructionId.ToString(),
            request.Amount,
            request.Currency,
            request.Reference,
            request.Payee is { } payee ? new WirePayee(payee.AccountNumber, payee.Name) : null), options: Json);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return BankPaymentResult.Failed(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => BankFeedFailure.Unauthorized,
                    HttpStatusCode.ServiceUnavailable => BankFeedFailure.Unavailable,
                    HttpStatusCode.NotFound => BankFeedFailure.UnknownAccount,
                    _ => BankFeedFailure.BadResponse
                });
            }

            return Read(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BankPaymentResult.Failed(BankFeedFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return BankPaymentResult.Failed(BankFeedFailure.Unreachable);
        }
    }

    private static Uri Address(BankConnectionSettings connection, BankPaymentRequest request) =>
        new($"{connection.BaseUrl!.ToString().TrimEnd('/')}/party/bank/accounts/{Uri.EscapeDataString(request.TrustAccountNumber)}/payments");

    private static BankPaymentResult Read(string body)
    {
        WireAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<WireAnswer>(body, Json);
        }
        catch (JsonException)
        {
            return BankPaymentResult.Failed(BankFeedFailure.BadResponse);
        }

        return answer switch
        {
            { Status: "Accepted", PaymentId: { Length: > 0 } paymentId } => BankPaymentResult.Accepted(paymentId),
            // R34: a reason the adapter does not know is Refused.
            { Status: "Rejected" } => BankPaymentResult.Rejected(
                Enum.TryParse<PaymentRejectReason>(answer.Reason, ignoreCase: false, out var reason) && Enum.IsDefined(reason)
                    ? reason
                    : PaymentRejectReason.Refused),
            _ => BankPaymentResult.Failed(BankFeedFailure.BadResponse)
        };
    }

    private sealed record WirePayment(
        [property: JsonPropertyName("instructionId")] string InstructionId,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("payee")] WirePayee? Payee);

    private sealed record WirePayee(
        [property: JsonPropertyName("accountNumber")] string AccountNumber,
        [property: JsonPropertyName("name")] string Name);

    private sealed record WireAnswer(
        [property: JsonPropertyName("paymentId")] string? PaymentId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("reason")] string? Reason);
}
