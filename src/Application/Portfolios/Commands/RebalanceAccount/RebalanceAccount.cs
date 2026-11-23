using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Rebalances;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;

/// <summary>Rebalances R10: proposes a rebalance of the account (trigger <c>Account</c>). A failed plan is stored as a
/// <c>Failed</c> rebalance and answered with its problem code.</summary>
[Authorize(Policy = Policies.TransactionsCreate)]
public class RebalanceAccountCommand : IRequest<RebalanceProposalDto>
{
    public Guid AccountId { get; init; }
}

public class RebalanceAccountCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, ProposeRebalance proposals)
    : IRequestHandler<RebalanceAccountCommand, RebalanceProposalDto>
{
    public async Task<RebalanceProposalDto> Handle(RebalanceAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
                          .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
                      ?? throw new NotFoundException("Account", request.AccountId);
        Rebalance? rebalance = null;
        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LockAccountAsync(account.Id, token);
            rebalance = await proposals.ProposeAsync(
                account, RebalanceTrigger.Account, currentUser.Person?.Id, publicId: null, token);
        }, cancellationToken);

        return RebalanceProposals.Answer(rebalance!);
    }
}
