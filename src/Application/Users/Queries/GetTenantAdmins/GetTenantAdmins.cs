using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Application.Users.Queries.GetTenantAdmins;

[Authorize(Policy = Policies.TenantAdminsManage)]
public record GetTenantAdminsQuery(
    int Page = 1,
    int PageSize = 20,
    string? TenantId = null,
    string? EnabledOnly = null,
    string? Search = null) : IRequest<PagedList<TenantAdminDto>>;

public class GetTenantAdminsQueryValidator : AbstractValidator<GetTenantAdminsQuery>
{
    public GetTenantAdminsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.EnabledOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("enabledOnly must be true or false.");
        RuleFor(query => query.TenantId)
            .Must(value => value is null || Guid.TryParse(value, out _))
            .WithMessage("tenantId must be a tenant PublicId.");
    }
}

public class GetTenantAdminsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTenantAdminsQuery, PagedList<TenantAdminDto>>
{
    public async Task<PagedList<TenantAdminDto>> Handle(
        GetTenantAdminsQuery request,
        CancellationToken cancellationToken)
    {
        var query =
            from user in db.Users.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on user.TenantId equals tenant.Id
            where user.Role == UserRole.TenantAdmin
            select new { user, tenant };

        if (Guid.TryParse(request.TenantId, out var tenantPublicId))
        {
            var tenant = await db.Tenants.AsNoTracking()
                .SingleOrDefaultAsync(row => row.PublicId == tenantPublicId, cancellationToken);
            if (tenant is null)
            {
                return Empty(request);
            }

            query = query.Where(row => row.tenant.Id == tenant.Id);
        }

        if (bool.TryParse(request.EnabledOnly, out var enabledOnly) && enabledOnly)
        {
            query = query.Where(row => row.user.IsActive);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            if (Guid.TryParse(search, out var publicId))
            {
                query = query.Where(row =>
                    row.user.PublicId == publicId
                    || row.user.Name.Contains(search)
                    || row.user.Email.Contains(search));
            }
            else
            {
                query = query.Where(row =>
                    row.user.Name.Contains(search)
                    || row.user.Email.Contains(search));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.user.Name)
            .ThenBy(row => row.user.Email)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<TenantAdminDto>
        {
            Items = items.Select(row => TenantAdminDto.From(row.user, row.tenant)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }

    private static PagedList<TenantAdminDto> Empty(GetTenantAdminsQuery request) => new()
    {
        Items = [],
        Page = request.Page,
        PageSize = request.PageSize,
        TotalCount = 0
    };
}
