using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Users.Queries.GetCustomers;

[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
public record GetCustomersQuery(
    int Page = 1,
    int PageSize = 20,
    string? EnabledOnly = null,
    string? Search = null,
    Guid? AdviserId = null) : IRequest<PagedList<CustomerDto>>;

public class GetCustomersQueryValidator : AbstractValidator<GetCustomersQuery>
{
    public GetCustomersQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.EnabledOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("enabledOnly must be true or false.");
        When(_ => !CustomerScope.ManagesTenant(currentUser), () =>
        {
            RuleFor(query => query.AdviserId)
                .Must(adviserId => adviserId is null || adviserId == currentUser.PublicId)
                .WithMessage("An Adviser cannot filter by another adviser.");
        });
    }
}

public class GetCustomersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCustomersQuery, PagedList<CustomerDto>>
{
    public async Task<PagedList<CustomerDto>> Handle(
        GetCustomersQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;

        var query =
            from user in db.Users.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on user.TenantId equals tenant.Id
            join adviser in db.Users.AsNoTracking() on user.AdviserId equals adviser.Id
            where user.Role == UserRole.Customer && user.TenantId == tenantId
            select new { user, tenant, adviser };

        var assignedAdviserId = CustomerScope.AssignedAdviserInternalId(currentUser);
        if (assignedAdviserId is not null)
        {
            query = query.Where(row => row.user.AdviserId == assignedAdviserId);
        }
        else if (request.AdviserId is { } filterAdviserId)
        {
            var filterAdviser = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                row => row.PublicId == filterAdviserId
                       && row.Role == UserRole.Adviser
                       && row.TenantId == tenantId,
                cancellationToken);
            if (filterAdviser is null)
            {
                return new PagedList<CustomerDto>
                {
                    Items = [],
                    Page = request.Page,
                    PageSize = request.PageSize,
                    TotalCount = 0
                };
            }

            query = query.Where(row => row.user.AdviserId == filterAdviser.Id);
        }

        if (bool.TryParse(request.EnabledOnly, out var enabledOnly) && enabledOnly)
        {
            query = query.Where(row => row.user.Status == UserStatus.Active);
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

        return new PagedList<CustomerDto>
        {
            Items = items.Select(row => CustomerDto.From(row.user, row.tenant, row.adviser)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
