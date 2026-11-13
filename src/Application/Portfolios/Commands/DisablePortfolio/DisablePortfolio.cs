using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.DisablePortfolio;

[Authorize(Policy = Policies.PortfoliosManage)]
public class DisablePortfolioCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; set; }
}

public class DisablePortfolioCommandValidator : AbstractValidator<DisablePortfolioCommand>
{
    public DisablePortfolioCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class DisablePortfolioCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DisablePortfolioCommand>
{
    public async Task Handle(DisablePortfolioCommand request, CancellationToken cancellationToken)
    {
        var query = db.Portfolios.Where(portfolio => portfolio.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(portfolio => portfolio.TenantId == currentUser.TenantId);
        }

        var portfolio = await query.SingleOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException("Portfolio", request.Id);
        await PortfolioBook.EnsureNotSharedAsync(db, currentUser, portfolio.Id, cancellationToken);
        if (!string.Equals(Convert.ToBase64String(portfolio.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var assignments = await db.AccountPortfolioAssignments
            .Where(assignment => assignment.PortfolioId == portfolio.Id && assignment.Status == AssignmentStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var assignment in assignments)
        {
            assignment.Remove();
        }

        portfolio.Disable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
