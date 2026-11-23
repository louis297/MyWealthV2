using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Application.Rebalances;

/// <summary>
/// Rebalances R4–R9: proposes a rebalance of one assigned Custodial account. The caller holds the Account lock and
/// commits. Every attempt is stored: <c>Proposed</c> with its lines and own targets, <c>NoChange</c>, or
/// <c>Failed</c> with its code and message. A newer proposal replaces a <c>Proposed</c> one; none is made while a
/// rebalance of the account is in progress.
/// </summary>
public sealed class ProposeRebalance(
    IApplicationDbContext db,
    RebalancePlanning planning,
    IBrokerConnections connections,
    TimeProvider time)
{
    public const string InProgressMessage = "A rebalance of this account is in progress. Cancel it or wait until it has ended.";
    public const string NotEligibleMessage = "The account is not an open Custodial account with an active portfolio.";
    public const string NoBrokerConnectionMessage = "The firm has no broker connection, so a rebalance cannot place orders.";

    /// <summary>The Account's open rebalance, or null.</summary>
    public Task<Rebalance?> OpenAsync(int accountId, CancellationToken cancellationToken) =>
        db.Rebalances.IgnoreQueryFilters()
            .Include(row => row.Targets)
            .SingleOrDefaultAsync(row => row.AccountId == accountId
                && (row.Status == RebalanceStatus.Proposed || row.Status == RebalanceStatus.Selling
                    || row.Status == RebalanceStatus.Buying), cancellationToken);

    /// <summary>R4–R9. Throws <c>account_not_eligible</c> when the account has no Active assignment, and
    /// <c>rebalance_in_progress</c> when a rebalance of it is in progress; both write nothing.</summary>
    public async Task<Rebalance> ProposeAsync(
        Account account,
        RebalanceTrigger trigger,
        int? byUserId,
        Guid? publicId,
        CancellationToken cancellationToken)
    {
        var assignment = await db.AccountPortfolioAssignments.IgnoreQueryFilters()
                             .SingleOrDefaultAsync(row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
                                 cancellationToken)
                         ?? throw new RebalanceException("account_not_eligible", NotEligibleMessage);

        var open = await OpenAsync(account.Id, cancellationToken);
        if (open is { IsInProgress: true })
        {
            throw new RebalanceException("rebalance_in_progress", InProgressMessage);
        }

        var now = time.GetUtcNow();
        if (open is not null)
        {
            // R4: the newer proposal replaces it, in the same database transaction. Saved first, so the filtered
            // unique index on the open rebalance never sees two.
            open.Cancel(RebalanceCancelReason.Superseded, now, anyOrderLive: false);
            await db.SaveChangesAsync(cancellationToken);
        }

        var portfolio = await db.Portfolios.IgnoreQueryFilters().Include(row => row.Lines)
            .SingleAsync(row => row.Id == assignment.PortfolioId, cancellationToken);
        var targets = RebalanceTargets.Of(portfolio);
        var subject = new RebalanceSubject(
            account.TenantId, account.Id, portfolio.Id, assignment.Id, trigger, targets.BaseCurrency,
            targets.MinimumTrade, targets.CashResidualPercent, targets.Lines, byUserId, publicId);

        Rebalance rebalance;
        if (await EligibilityProblemAsync(account, portfolio, cancellationToken) is { } problem)
        {
            rebalance = Rebalance.Fail(subject, problem.Code, problem.Message, now);
        }
        else
        {
            var planned = await planning.PlanAsync(account, targets, sizeBuysByReservation: false, now, cancellationToken);
            rebalance = planned.Plan.Status switch
            {
                RebalancePlanOutcome.Failed => Rebalance.Fail(subject, planned.Plan.Code!, planned.Plan.Message!, now),
                RebalancePlanOutcome.NoChange => Rebalance.NoChange(subject, planned.TotalValue ?? 0m, planned.Cash, now),
                _ => Rebalance.Propose(subject, planned.TotalValue ?? 0m, planned.Cash, planned.Lines(), now)
            };
        }

        rebalance.CreatedBy = SystemActor.Name;
        db.Rebalances.Add(rebalance);
        await db.SaveChangesAsync(cancellationToken);
        return rebalance;
    }

    /// <summary>R6: the account, its Customer, its Tenant, the portfolio and the broker connection. Checked when a
    /// rebalance is proposed, when it is confirmed and before every wave.</summary>
    public async Task<(string Code, string Message)?> EligibilityProblemAsync(
        Account account,
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        if (account.Status != AccountStatus.Open || account.CustodyMode != CustodyMode.Custodial)
        {
            return ("account_not_eligible", NotEligibleMessage);
        }

        var customerStatus = await db.Users.IgnoreQueryFilters()
            .Where(row => row.Id == account.CustomerId)
            .Select(row => row.Status)
            .SingleAsync(cancellationToken);
        if (customerStatus == UserStatus.Disabled)
        {
            return ("account_not_eligible", "The account's customer is disabled. Enable the customer first.");
        }

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .Where(row => row.Id == account.TenantId)
            .Select(row => new { row.IsActive, row.BrokerConnection })
            .SingleAsync(cancellationToken);
        if (!tenant.IsActive)
        {
            return ("account_not_eligible", "The firm is disabled.");
        }

        if (!portfolio.IsActive)
        {
            return ("portfolio_inactive", "The portfolio is disabled.");
        }

        if (tenant.BrokerConnection is null || !connections.IsConfigured(tenant.BrokerConnection))
        {
            return ("no_broker_connection", NoBrokerConnectionMessage);
        }

        return null;
    }
}
