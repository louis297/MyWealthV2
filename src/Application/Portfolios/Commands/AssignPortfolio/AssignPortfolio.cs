using MyWealthV2.Application.AccountClosures;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Rebalances;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.AssignPortfolio;

/// <summary>Portfolios R4, R5 and rebalances R10, R23: assigns a portfolio and proposes a rebalance (trigger
/// <c>Assign</c>). A failed plan still leaves the assignment Active.</summary>
[Authorize(Policy = Policies.TransactionsCreate)]
public class AssignPortfolioCommand : IRequest<RebalanceProposalDto>
{
    public Guid AccountId { get; set; }

    public Guid PortfolioId { get; init; }
}

public class AssignPortfolioCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ProposeRebalance proposals,
    TimeProvider time)
    : IRequestHandler<AssignPortfolioCommand, RebalanceProposalDto>
{
    public async Task<RebalanceProposalDto> Handle(AssignPortfolioCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
            .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.AccountId);
        // Custody modes R14: a Custodial account is always Brokerage (R3), so the custody mode replaces the type test.
        if (account.Status != AccountStatus.Open || account.CustodyMode != CustodyMode.Custodial)
        {
            throw new RebalanceException(
                "account_not_eligible",
                "Only an open Custodial account can be assigned a portfolio.");
        }

        // Account closure R13: a closing account is not assigned a portfolio.
        if (await AccountClosing.IsClosingAsync(db, account.Id, cancellationToken))
        {
            throw new RebalanceException(
                "account_not_eligible",
                "Only an open Custodial account that is not being closed can be assigned a portfolio.");
        }

        // Rebalances R23: an assignment is not replaced while its rebalance is in progress.
        if (await proposals.OpenAsync(account.Id, cancellationToken) is { IsInProgress: true })
        {
            throw new RebalanceException("rebalance_in_progress", ProposeRebalance.InProgressMessage);
        }

        // Orders R49: a person's live order and an assignment never share an account, so a rebalance never plans
        // around a reservation it did not make.
        if (await Orders.OrderReservations.HasLiveOrderAsync(db, account.Id, cancellationToken))
        {
            throw new RebalanceException(
                "account_not_eligible",
                "Cancel the account's open orders before assigning a portfolio.");
        }

        var portfolio = await db.Portfolios.SingleOrDefaultAsync(
            row => row.PublicId == request.PortfolioId && row.TenantId == account.TenantId,
            cancellationToken) ?? throw new NotFoundException("Portfolio", request.PortfolioId);
        if (!portfolio.IsActive)
        {
            throw new RebalanceException("portfolio_inactive", "The portfolio is disabled.");
        }

        var current = await db.AccountPortfolioAssignments.SingleOrDefaultAsync(
            row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
            cancellationToken);
        if (current is not null && current.PortfolioId == portfolio.Id)
        {
            throw new AssignmentConflictException();
        }

        Rebalance? rebalance = null;
        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LockAccountAsync(account.Id, token);
            var now = time.GetUtcNow();
            // R23: a proposal of the assignment it replaces is cancelled, as unassign does (R22).
            if (await proposals.OpenAsync(account.Id, token) is { } open)
            {
                if (open.IsInProgress)
                {
                    throw new RebalanceException("rebalance_in_progress", ProposeRebalance.InProgressMessage);
                }

                open.Cancel(RebalanceCancelReason.Unassigned, now, anyOrderLive: false);
            }

            var assignment = AccountPortfolioAssignment.Assign(
                current,
                account.TenantId,
                account.Id,
                portfolio.Id,
                currentUser.PublicId?.ToString("D") ?? "system",
                now);
            db.AccountPortfolioAssignments.Add(assignment);
            await db.SaveChangesAsync(token);
            rebalance = await proposals.ProposeAsync(account, RebalanceTrigger.Assign, currentUser.Person?.Id, publicId: null, token);
        }, cancellationToken);

        return RebalanceProposals.Answer(rebalance!);
    }
}
