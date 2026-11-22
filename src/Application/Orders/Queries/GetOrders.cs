using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Orders.Queries;

/// <summary>Orders R16: the orders of the Accounts in the caller's scope, newest placed first, without their fills.</summary>
[Authorize(Policy = Policies.OrdersRead)]
public sealed record GetOrdersQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    Guid? InstrumentId = null,
    string? Side = null,
    string? Status = null,
    bool? PendingFill = null,
    Guid? TenantId = null) : IRequest<PagedList<OrderDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Orders R16: one order in the caller's scope, with its fills.</summary>
[Authorize(Policy = Policies.OrdersRead)]
public sealed record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto>;

public class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, PagedList<OrderDto>>
{
    public Task<PagedList<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
