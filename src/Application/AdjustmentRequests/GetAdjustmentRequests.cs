using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Adjustment review R20: the Tenant's adjustment requests, newest requested first.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed record GetAdjustmentRequestsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    Guid? TenantId = null) : IRequest<PagedList<AdjustmentRequestDto>>;

/// <summary>Adjustment review R20: one adjustment request of the Tenant.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed record GetAdjustmentRequestByIdQuery(Guid Id) : IRequest<AdjustmentRequestDto>;

public class GetAdjustmentRequestsQueryHandler : IRequestHandler<GetAdjustmentRequestsQuery, PagedList<AdjustmentRequestDto>>
{
    public Task<PagedList<AdjustmentRequestDto>> Handle(GetAdjustmentRequestsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class GetAdjustmentRequestByIdQueryHandler : IRequestHandler<GetAdjustmentRequestByIdQuery, AdjustmentRequestDto>
{
    public Task<AdjustmentRequestDto> Handle(GetAdjustmentRequestByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
