using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>The bank payments adapter for <c>Adapter = Simulator</c> (withdrawals R49–R51, R58).</summary>
public sealed class SimulatorBankPayments(IHttpClientFactory httpClients) : IBankPaymentsAdapter
{
    private readonly IHttpClientFactory _httpClients = httpClients;

    public const string HttpClientName = "bank-payments-simulator";

    public string Adapter => BankConnectionsConfiguration.SimulatorAdapter;

    public Task<BankPaymentResult> SendAsync(
        BankConnectionSettings connection,
        BankPaymentRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(BankPaymentResult.Failed(BankFeedFailure.Unreachable));
}
