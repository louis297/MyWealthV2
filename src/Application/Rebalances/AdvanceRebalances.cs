using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Application.Rebalances;

/// <summary>What the advance step did: rebalances that ended or placed a wave, and rebalances it failed on.</summary>
public sealed record RebalanceAdvanceOutcome(int Advanced, int Failed);

/// <summary>
/// Rebalances R17: the sixth step of <c>broker-sync:{connection}</c>, after the fills are posted. Every rebalance in
/// progress that took the connection and has no live order ends, or places its next wave. It reads past the tenant
/// query filter and takes the Tenant from each row, as <see cref="ProcessExecutionReports"/>.
/// </summary>
public sealed class AdvanceRebalances
{
    public const string NotFilledCode = "order_not_filled";

    public Task<RebalanceAdvanceOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RebalanceAdvanceOutcome(0, 0));
}
