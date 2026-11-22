using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>What one apply run did: entitlements posted, not applied, and failed (left <c>Pending</c>).</summary>
public sealed record ApplyCorporateActionsOutcome(int Posted, int NotApplied, int Failed)
{
    public bool HasFailures => Failed > 0;
}

/// <summary>
/// The apply step (docs/features/corporate-actions.md R23–R25, R28–R31): makes every due <c>Manual</c> corporate
/// action effective, then posts each <c>Pending</c> entitlement of every <c>Effective</c> action, oldest
/// <c>effectiveAt</c> first and in account <c>Id</c> order, each in its own database transaction under the Account
/// lock. The one place an entitlement is posted. Runs as the worker's system user and names each Tenant itself.
/// </summary>
public sealed class ApplyCorporateActions
{
    public Task<ApplyCorporateActionsOutcome> RunAsync(string? connection = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
