using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>
/// The custodian elections adapter for <c>Adapter = Simulator</c> (dividends R65): posts one election to the simulator
/// custodian's party route (R58) with the connection's key and timeout. The body is built from the stored dividend
/// only, so a 409 cannot come from a changed body and is a bad response.
/// </summary>
public sealed class SimulatorCustodianElections(IHttpClientFactory httpClients) : ICustodianElectionsAdapter
{
    public const string HttpClientName = "custodian-elections-simulator";

    private static readonly JsonSerializerOptions Json = new();

    public string Adapter => CustodianConnectionsConfiguration.SimulatorAdapter;

    public async Task<CustodianElectionResult> SubmitAsync(
        CustodianConnectionSettings connection, CustodianElectionRequest request, CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var message = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"{connection.BaseUrl!.ToString().TrimEnd('/')}/party/custodian/accounts/{Uri.EscapeDataString(request.AccountNumber)}/elections"));
        message.Headers.Add(SimulatorCustodianFeed.ApiKeyHeader, connection.ApiKey);
        message.Content = JsonContent.Create(new WireElection(
            request.InstructionId.ToString(), request.CorporateActionId, request.Symbol, request.Currency, request.Quantity), options: Json);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return CustodianElectionResult.Failed(SimulatorCustodianFeed.Failure(response.StatusCode));
            }

            return Read(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CustodianElectionResult.Failed(CustodianFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return CustodianElectionResult.Failed(CustodianFailure.Unreachable);
        }
    }

    private static CustodianElectionResult Read(string body)
    {
        WireAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<WireAnswer>(body, Json);
        }
        catch (JsonException)
        {
            return CustodianElectionResult.Failed(CustodianFailure.BadResponse);
        }

        return answer?.Status switch
        {
            "Accepted" => CustodianElectionResult.Accepted(),
            // R65: a reason the adapter does not know is Other.
            "Rejected" => CustodianElectionResult.Rejected(
                Enum.TryParse<ElectionRejectReason>(answer.Reason, ignoreCase: false, out var reason) && Enum.IsDefined(reason)
                    && !int.TryParse(answer.Reason, out _)
                    ? reason
                    : ElectionRejectReason.Other),
            _ => CustodianElectionResult.Failed(CustodianFailure.BadResponse)
        };
    }

    private sealed record WireElection(
        [property: JsonPropertyName("instructionId")] string InstructionId,
        [property: JsonPropertyName("corporateActionId")] string CorporateActionId,
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("quantity")] decimal Quantity);

    private sealed record WireAnswer(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("reason")] string? Reason);
}
