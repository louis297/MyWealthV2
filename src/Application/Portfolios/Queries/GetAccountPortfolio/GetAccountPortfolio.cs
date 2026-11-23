using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Rebalances;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Queries.GetAccountPortfolio;

public record GetAccountPortfolioQuery(Guid AccountId) : IRequest<AccountPortfolioDto>;

public class GetAccountPortfolioQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountPortfolioQuery, AccountPortfolioDto>
{
    public async Task<AccountPortfolioDto> Handle(GetAccountPortfolioQuery request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
            .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.AccountId);
        var assignment = await db.AccountPortfolioAssignments.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
                cancellationToken) ?? throw new NotFoundException("Portfolio", request.AccountId);
        var portfolio = await db.Portfolios.AsNoTracking()
            .SingleAsync(row => row.Id == assignment.PortfolioId, cancellationToken);
        // Rebalances R25: the newest rebalance of the assignment, or null.
        var rebalance = await db.Rebalances.AsNoTracking()
            .Where(row => row.AssignmentId == assignment.Id)
            .OrderByDescending(row => row.ProposedAt)
            .ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return new AccountPortfolioDto(
            account.PublicId,
            portfolio.PublicId,
            assignment.Status.ToString(),
            rebalance is null
                ? null
                : new AccountRebalanceDto(
                    rebalance.PublicId,
                    rebalance.Status.ToString(),
                    rebalance.Status is RebalanceStatus.Failed or RebalanceStatus.Incomplete && rebalance.FailureCode is { } code
                        ? new RebalanceFailureDto(code, rebalance.FailureMessage ?? code)
                        : null,
                    rebalance.ProposedAt,
                    rebalance.EndedAt));
    }
}
