using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios.Queries.GetPortfolioById;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Queries.GetPortfolios;

[Authorize(Policy = Policies.PortfoliosRead)]
public record GetPortfoliosQuery(int Page = 1, int PageSize = 20, Guid? TenantId = null)
    : IRequest<PagedList<PortfolioDto>>;

public class GetPortfoliosQueryValidator : AbstractValidator<GetPortfoliosQuery>
{
    public GetPortfoliosQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).NotEmpty().WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class GetPortfoliosQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetPortfoliosQuery, PagedList<PortfolioDto>>
{
    public async Task<PagedList<PortfolioDto>> Handle(GetPortfoliosQuery request, CancellationToken cancellationToken)
    {
        int tenantId;
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            var tenant = await db.Tenants.SingleOrDefaultAsync(
                             row => row.PublicId == request.TenantId, cancellationToken)
                         ?? throw new NotFoundException("Tenant", request.TenantId!);
            tenantId = tenant.Id;
        }
        else
        {
            tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        }

        var query = db.Portfolios
            .Include(portfolio => portfolio.Lines)
            .Where(portfolio => portfolio.TenantId == tenantId);
        var totalCount = await query.CountAsync(cancellationToken);
        var portfolios = await query
            .OrderBy(portfolio => portfolio.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var tenantRow = await db.Tenants.SingleAsync(row => row.Id == tenantId, cancellationToken);
        var instrumentIds = await GetPortfolioByIdQueryHandler.InstrumentPublicIdsAsync(
            db,
            portfolios.SelectMany(portfolio => portfolio.Lines.Select(line => line.InstrumentId)),
            cancellationToken);

        return new PagedList<PortfolioDto>
        {
            Items = portfolios.Select(portfolio => PortfolioDto.From(portfolio, tenantRow, instrumentIds)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
