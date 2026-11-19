using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccounts;

/// <summary>Client money accounts R9: sorted by currency, then active first, then newest first.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public record GetClientMoneyAccountsQuery(
    Guid TenantId,
    int Page = 1,
    int PageSize = 20,
    string? EnabledOnly = null) : IRequest<PagedList<ClientMoneyAccountDto>>;

public class GetClientMoneyAccountsQueryValidator : AbstractValidator<GetClientMoneyAccountsQuery>
{
    public GetClientMoneyAccountsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.EnabledOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("enabledOnly must be true or false.");
    }
}

public class GetClientMoneyAccountsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetClientMoneyAccountsQuery, PagedList<ClientMoneyAccountDto>>
{
    public async Task<PagedList<ClientMoneyAccountDto>> Handle(
        GetClientMoneyAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = await ClientMoneyAccountLookup.TenantAsync(db, request.TenantId, cancellationToken);

        var query = db.ClientMoneyAccounts.AsNoTracking().Where(row => row.TenantId == tenant.Id);
        if (bool.TryParse(request.EnabledOnly, out var enabledOnly) && enabledOnly)
        {
            query = query.Where(row => row.IsActive);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.Currency)
            .ThenByDescending(row => row.IsActive)
            .ThenByDescending(row => row.Created)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<ClientMoneyAccountDto>
        {
            Items = items.Select(row => ClientMoneyAccountDto.From(row, tenant)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
