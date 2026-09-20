using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Users.Queries.GetAdvisers;

[Authorize(Policy = Policies.AdvisersManage)]
public record GetAdvisersQuery(
    int Page = 1,
    int PageSize = 20,
    string? EnabledOnly = null,
    string? Search = null) : IRequest<PagedList<AdviserDto>>;

public class GetAdvisersQueryValidator : AbstractValidator<GetAdvisersQuery>
{
    public GetAdvisersQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.EnabledOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("enabledOnly must be true or false.");
    }
}

public class GetAdvisersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAdvisersQuery, PagedList<AdviserDto>>
{
    public async Task<PagedList<AdviserDto>> Handle(
        GetAdvisersQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;

        var query =
            from user in db.Users.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on user.TenantId equals tenant.Id
            where user.Role == UserRole.Adviser && user.TenantId == tenantId
            select new { user, tenant };

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

        return new PagedList<AdviserDto>
        {
            Items = items.Select(row => AdviserDto.From(row.user, row.tenant)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
