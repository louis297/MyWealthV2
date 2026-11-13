using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.RebalancePortfolio;

[Authorize(Policy = Policies.TransactionsCreate)]
public class RebalancePortfolioCommand : IRequest<RebalanceAcceptedDto>
{
    public Guid Id { get; set; }
}

public class RebalancePortfolioCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IRebalanceScheduler scheduler)
    : IRequestHandler<RebalancePortfolioCommand, RebalanceAcceptedDto>
{
    public async Task<RebalanceAcceptedDto> Handle(RebalancePortfolioCommand request, CancellationToken cancellationToken)
    {
        var query = db.Portfolios.Where(portfolio => portfolio.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(portfolio => portfolio.TenantId == currentUser.TenantId);
        }

        var portfolio = await query.SingleOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException("Portfolio", request.Id);
        var accountIds = await PortfolioBook.ActiveAccountIdsAsync(db, currentUser, portfolio.Id, cancellationToken);
        var handles = accountIds
            .Select(accountId => new RebalanceAccountHandleDto(accountId, Guid.NewGuid()))
            .ToList();
        foreach (var handle in handles)
        {
            scheduler.Enqueue(handle.AccountId, handle.Handle);
        }

        return new RebalanceAcceptedDto(Guid.NewGuid(), handles);
    }
}
