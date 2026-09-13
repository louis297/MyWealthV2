using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Application.Tenants.Queries.GetTenants;

[Authorize(Policy = Policies.TenantsManage)]
public record GetTenantsQuery(
    int Page = 1,
    int PageSize = 20,
    string? IsEnabled = null,
    string? Search = null) : IRequest<PagedList<TenantDto>>;

public class GetTenantsQueryValidator : AbstractValidator<GetTenantsQuery>
{
    public GetTenantsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.IsEnabled)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("isEnabled must be true or false.");
    }
}

public class GetTenantsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTenantsQuery, PagedList<TenantDto>>
{
    public async Task<PagedList<TenantDto>> Handle(GetTenantsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Tenants.AsNoTracking();

        if (bool.TryParse(request.IsEnabled, out var isEnabled))
        {
            query = query.Where(tenant => tenant.IsEnabled == isEnabled);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            if (Guid.TryParse(search, out var publicId))
            {
                query = query.Where(tenant =>
                    tenant.PublicId == publicId
                    || tenant.Name.Contains(search)
                    || tenant.Code.Contains(search));
            }
            else
            {
                query = query.Where(tenant =>
                    tenant.Name.Contains(search)
                    || tenant.Code.Contains(search));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(tenant => tenant.Name)
            .ThenBy(tenant => tenant.Code)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<TenantDto>
        {
            Items = items.Select(TenantDto.From).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
