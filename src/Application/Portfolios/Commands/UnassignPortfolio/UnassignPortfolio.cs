using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.UnassignPortfolio;

[Authorize(Policy = Policies.TransactionsCreate)]
public class UnassignPortfolioCommand : IRequest
{
    public Guid AccountId { get; set; }
}

public class UnassignPortfolioCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UnassignPortfolioCommand>
{
    public async Task Handle(UnassignPortfolioCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
            .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.AccountId);
        var assignment = await db.AccountPortfolioAssignments.SingleOrDefaultAsync(
            row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
            cancellationToken) ?? throw new NotFoundException("Portfolio", request.AccountId);
        assignment.Remove();
        await db.SaveChangesAsync(cancellationToken);
    }
}
