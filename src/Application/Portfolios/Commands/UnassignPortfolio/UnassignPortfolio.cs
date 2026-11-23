using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Rebalances;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.UnassignPortfolio;

[Authorize(Policy = Policies.TransactionsCreate)]
public class UnassignPortfolioCommand : IRequest
{
    public Guid AccountId { get; set; }
}

public class UnassignPortfolioCommandHandler(
    IApplicationDbContext db, ICurrentUser currentUser, ProposeRebalance proposals, TimeProvider time)
    : IRequestHandler<UnassignPortfolioCommand>
{
    public async Task Handle(UnassignPortfolioCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
            .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.AccountId);
        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LockAccountAsync(account.Id, token);
            var assignment = await db.AccountPortfolioAssignments.SingleOrDefaultAsync(
                row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
                token) ?? throw new NotFoundException("Portfolio", request.AccountId);
            // Rebalances R22: never refused because of a rebalance; the open one is cancelled by the platform.
            if (await proposals.OpenAsync(account.Id, token) is { } open)
            {
                await RebalanceCancellation.CancelAsync(db, open, byUserId: null, note: null, time.GetUtcNow(), token);
            }

            assignment.Remove();
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }
}
