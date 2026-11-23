using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

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

public class GetRebalancesQueryHandler : IRequestHandler<GetRebalancesQuery, PagedList<RebalanceDto>>
{
    public Task<PagedList<RebalanceDto>> Handle(GetRebalancesQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class GetRebalanceByIdQueryHandler : IRequestHandler<GetRebalanceByIdQuery, RebalanceDto>
{
    public Task<RebalanceDto> Handle(GetRebalanceByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
