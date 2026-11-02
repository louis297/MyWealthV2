using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.AssignPortfolio;

[Authorize(Policy = Policies.TransactionsCreate)]
public class AssignPortfolioCommand : IRequest<RebalanceResultDto>
{
    public Guid AccountId { get; set; }

    public Guid PortfolioId { get; init; }
}

public class AssignPortfolioCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ISender sender,
    TimeProvider time)
    : IRequestHandler<AssignPortfolioCommand, RebalanceResultDto>
{
    public async Task<RebalanceResultDto> Handle(AssignPortfolioCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
            .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.AccountId);
        if (account.Status != AccountStatus.Open || account.Type is not (AccountType.Brokerage or AccountType.Other))
        {
            throw new RebalanceException(
                "account_not_eligible",
                "Only an open Brokerage or Other account can be assigned a portfolio.");
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

        var assignment = AccountPortfolioAssignment.Assign(
            current,
            account.TenantId,
            account.Id,
            portfolio.Id,
            currentUser.PublicId?.ToString("D") ?? "system",
            time.GetUtcNow());
        db.AccountPortfolioAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new RebalanceAccountCommand
        {
            AccountId = account.PublicId,
            Handle = Guid.NewGuid()
        }, cancellationToken);
    }
}
