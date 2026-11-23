using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.TradeSettlements.Queries;

/// <summary>Trade-settlement R16: the trade settlements of the caller's Tenant, newest first.</summary>
[Authorize(Policy = Policies.TradeSettlementsManage)]
public sealed record GetTradeSettlementsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? ClientMoneyAccountId = null,
    bool? Open = null,
    Guid? TenantId = null) : IRequest<PagedList<TradeSettlementDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Trade-settlement R16: one trade settlement with its fills and payments.</summary>
[Authorize(Policy = Policies.TradeSettlementsManage)]
public sealed record GetTradeSettlementByIdQuery(Guid Id) : IRequest<TradeSettlementDto>;

public class GetTradeSettlementsQueryValidator : AbstractValidator<GetTradeSettlementsQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<TradeSettlementStatus>();

    public GetTradeSettlementsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetTradeSettlementsQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
    }
}

public class GetTradeSettlementsQueryHandler : IRequestHandler<GetTradeSettlementsQuery, PagedList<TradeSettlementDto>>
{
    public Task<PagedList<TradeSettlementDto>> Handle(GetTradeSettlementsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class GetTradeSettlementByIdQueryHandler : IRequestHandler<GetTradeSettlementByIdQuery, TradeSettlementDto>
{
    public Task<TradeSettlementDto> Handle(GetTradeSettlementByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
