using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Rebalances.Queries;

/// <summary>Rebalances R27: the rebalances of the Accounts in the caller's scope, newest proposed first, without
/// their targets, lines and orders.</summary>
[Authorize(Policy = Policies.RebalancesRead)]
public sealed record GetRebalancesQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    Guid? PortfolioId = null,
    string? Status = null,
    bool? Open = null,
    Guid? TenantId = null) : IRequest<PagedList<RebalanceDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Rebalances R27: one rebalance in the caller's scope, with its targets, lines and orders.</summary>
[Authorize(Policy = Policies.RebalancesRead)]
public sealed record GetRebalanceByIdQuery(Guid Id) : IRequest<RebalanceDto>;

public class GetRebalancesQueryValidator : AbstractValidator<GetRebalancesQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<RebalanceStatus>();

    public GetRebalancesQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetRebalancesQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
    }
}

public class GetRebalancesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetRebalancesQuery, PagedList<RebalanceDto>>
{
    public async Task<PagedList<RebalanceDto>> Handle(GetRebalancesQuery request, CancellationToken cancellationToken)
    {
        var query = RebalanceReader.Scope(db, currentUser).AsNoTracking();
        if (request.AccountId is Guid accountId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId && account.PublicId == accountId));
        }

        if (request.CustomerId is Guid customerId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId
                && db.Users.Any(user => user.Id == account.CustomerId && user.PublicId == customerId)));
        }

        if (request.PortfolioId is Guid portfolioId)
        {
            query = query.Where(row => db.Portfolios.Any(portfolio => portfolio.Id == row.PortfolioId && portfolio.PublicId == portfolioId));
        }

        var statuses = GetRebalancesQuery.Names(request.Status)
            .Select(name => Enum.Parse<RebalanceStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        if (request.Open == true)
        {
            query = query.Where(row => row.Status == RebalanceStatus.Proposed || row.Status == RebalanceStatus.Selling
                || row.Status == RebalanceStatus.Buying);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.ProposedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<RebalanceDto>
        {
            Items = await RebalanceReader.ToDtosAsync(db, rows, withDetail: false, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetRebalanceByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetRebalanceByIdQuery, RebalanceDto>
{
    public async Task<RebalanceDto> Handle(GetRebalanceByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await RebalanceReader.Scope(db, currentUser).AsNoTracking()
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Rebalance", request.Id);
        return (await RebalanceReader.ToDtosAsync(db, [row], withDetail: true, cancellationToken)).Single();
    }
}
