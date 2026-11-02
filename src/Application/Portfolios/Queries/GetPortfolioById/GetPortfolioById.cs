using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Queries.GetPortfolioById;

[Authorize(Policy = Policies.PortfoliosRead)]
public record GetPortfolioByIdQuery(Guid Id) : IRequest<PortfolioDto>;

public class GetPortfolioByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetPortfolioByIdQuery, PortfolioDto>
{
    public async Task<PortfolioDto> Handle(GetPortfolioByIdQuery request, CancellationToken cancellationToken)
    {
        var query = db.Portfolios
            .Include(portfolio => portfolio.Lines)
            .Where(portfolio => portfolio.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(portfolio => portfolio.TenantId == currentUser.TenantId);
        }

        var portfolio = await query.SingleOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException("Portfolio", request.Id);
        var tenant = await db.Tenants.SingleAsync(row => row.Id == portfolio.TenantId, cancellationToken);
        var instrumentIds = await InstrumentPublicIdsAsync(db, portfolio.Lines.Select(line => line.InstrumentId), cancellationToken);
        return PortfolioDto.From(portfolio, tenant, instrumentIds);
    }

    internal static async Task<Dictionary<int, Guid>> InstrumentPublicIdsAsync(
        IApplicationDbContext db,
        IEnumerable<int> instrumentIds,
        CancellationToken cancellationToken)
    {
        var ids = instrumentIds.Distinct().ToList();
        return await db.Instruments.AsNoTracking()
            .Where(instrument => ids.Contains(instrument.Id))
            .ToDictionaryAsync(instrument => instrument.Id, instrument => instrument.PublicId, cancellationToken);
    }
}
