using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Custodian;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.SafekeepingAccounts;

/// <summary>R4: one tenant's safekeeping accounts, sorted by currency, then active first, then newest first.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public record GetSafekeepingAccountsQuery(Guid TenantId, int Page = 1, int PageSize = 20) : IRequest<PagedList<SafekeepingAccountDto>>;

public class GetSafekeepingAccountsQueryValidator : AbstractValidator<GetSafekeepingAccountsQuery>
{
    public GetSafekeepingAccountsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetSafekeepingAccountsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetSafekeepingAccountsQuery, PagedList<SafekeepingAccountDto>>
{
    public async Task<PagedList<SafekeepingAccountDto>> Handle(GetSafekeepingAccountsQuery request, CancellationToken cancellationToken)
    {
        var tenant = await SafekeepingAccountLookup.TenantAsync(db, request.TenantId, cancellationToken);
        var query = db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking().Where(row => row.TenantId == tenant.Id);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.Currency)
            .ThenByDescending(row => row.IsActive)
            .ThenByDescending(row => row.Created)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var ids = items.Select(row => row.Id).ToList();
        var cursors = await db.CustodianFeedCursors.IgnoreQueryFilters().AsNoTracking()
            .Where(row => ids.Contains(row.SafekeepingAccountId))
            .ToDictionaryAsync(row => row.SafekeepingAccountId, cancellationToken);
        return new PagedList<SafekeepingAccountDto>
        {
            Items = items.Select(row => SafekeepingAccountDto.From(row, cursors.GetValueOrDefault(row.Id))).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

[Authorize(Policy = Policies.TenantsManage)]
public record GetSafekeepingAccountByIdQuery(Guid TenantId, Guid Id) : IRequest<SafekeepingAccountDto>;

public class GetSafekeepingAccountByIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetSafekeepingAccountByIdQuery, SafekeepingAccountDto>
{
    public async Task<SafekeepingAccountDto> Handle(GetSafekeepingAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var tenant = await SafekeepingAccountLookup.TenantAsync(db, request.TenantId, cancellationToken);
        var row = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
                      .SingleOrDefaultAsync(r => r.TenantId == tenant.Id && r.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Safekeeping account", request.Id);
        var cursor = await db.CustodianFeedCursors.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(r => r.SafekeepingAccountId == row.Id, cancellationToken);
        return SafekeepingAccountDto.From(row, cursor);
    }
}

/// <summary>R7: the configured custodian connections, every entry on page 1.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public sealed record GetCustodianConnectionsQuery : IRequest<PagedList<CustodianConnectionDto>>;

public sealed record CustodianConnectionDto(string Name, string Adapter);

public class GetCustodianConnectionsQueryHandler(ICustodianConnections connections)
    : IRequestHandler<GetCustodianConnectionsQuery, PagedList<CustodianConnectionDto>>
{
    public Task<PagedList<CustodianConnectionDto>> Handle(GetCustodianConnectionsQuery request, CancellationToken cancellationToken)
    {
        var items = connections.All
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => new CustodianConnectionDto(entry.Name, entry.Adapter))
            .ToList();
        return Task.FromResult(new PagedList<CustodianConnectionDto>
        {
            Items = items,
            Page = 1,
            PageSize = Math.Max(20, items.Count),
            TotalCount = items.Count
        });
    }
}
