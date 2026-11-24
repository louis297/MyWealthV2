using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>
/// The custodian transfers adapter for <c>Adapter = Simulator</c> (in-kind-transfers R74): posts one instruction to the
/// simulator custodian's party route (R62) with the connection's key and timeout. The body is built from the stored
/// transfer only, so a 409 cannot come from a changed body and is a bad response.
/// </summary>
public sealed class SimulatorCustodianTransfers(IHttpClientFactory httpClients) : ICustodianTransfersAdapter
{
    public const string HttpClientName = "custodian-transfers-simulator";

    private static readonly JsonSerializerOptions Json = new();

    public string Adapter => CustodianConnectionsConfiguration.SimulatorAdapter;

    public async Task<CustodianTransferResult> SendAsync(
        CustodianConnectionSettings connection, CustodianTransferRequest request, CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var message = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"{connection.BaseUrl!.ToString().TrimEnd('/')}/party/custodian/accounts/{Uri.EscapeDataString(request.AccountNumber)}/transfers"));
        message.Headers.Add(SimulatorCustodianFeed.ApiKeyHeader, connection.ApiKey);
        message.Content = JsonContent.Create(new WireTransfer(
            request.InstructionId.ToString(),
            request.Direction.ToString(),
            request.Symbol,
            request.Currency,
            request.Quantity,
            new WireCounterparty(request.Counterparty.Name, request.Counterparty.Account, request.Counterparty.HolderName)), options: Json);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return CustodianTransferResult.Failed(SimulatorCustodianFeed.Failure(response.StatusCode));
            }

            return Read(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CustodianTransferResult.Failed(CustodianFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return CustodianTransferResult.Failed(CustodianFailure.Unreachable);
        }
    }

    private static CustodianTransferResult Read(string body)
    {
        WireAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<WireAnswer>(body, Json);
        }
        catch (JsonException)
        {
            return CustodianTransferResult.Failed(CustodianFailure.BadResponse);
        }

        return answer switch
        {
            { Status: "Accepted", TransferId: { Length: > 0 } transferId } => CustodianTransferResult.Accepted(transferId),
            // R35: a reason the adapter does not know is Refused.
            { Status: "Rejected" } => CustodianTransferResult.Rejected(
                Enum.TryParse<TransferRejectReason>(answer.Reason, ignoreCase: false, out var reason) && Enum.IsDefined(reason)
                    ? reason
                    : TransferRejectReason.Refused),
            _ => CustodianTransferResult.Failed(CustodianFailure.BadResponse)
        };
    }

    private sealed record WireTransfer(
        [property: JsonPropertyName("instructionId")] string InstructionId,
        [property: JsonPropertyName("direction")] string Direction,
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("counterparty")] WireCounterparty Counterparty);

    private sealed record WireCounterparty(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("account")] string Account,
        [property: JsonPropertyName("holderName")] string HolderName);

    private sealed record WireAnswer(
        [property: JsonPropertyName("transferId")] string? TransferId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("reason")] string? Reason);
}
