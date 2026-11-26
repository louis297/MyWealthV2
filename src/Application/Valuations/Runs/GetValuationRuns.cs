using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>Valuation-run R22: a Tenant's runs, newest start first. A TenantAdmin reads the current Tenant's; a
/// SystemAdmin names one.</summary>
[Authorize(Policy = Policies.ValuationRunsRead)]
public record GetValuationRunsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? TenantId = null,
    string? Trigger = null,
    string? From = null,
    string? To = null) : IRequest<PagedList<ValuationRunDto>>;

public class GetValuationRunsQueryHandler : IRequestHandler<GetValuationRunsQuery, PagedList<ValuationRunDto>>
{
    public Task<PagedList<ValuationRunDto>> Handle(GetValuationRunsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

/// <summary>Valuation-run R22, R23: one run of the current Tenant (TenantAdmin) or of any Tenant (SystemAdmin).</summary>
[Authorize(Policy = Policies.ValuationRunsRead)]
public record GetValuationRunByIdQuery(Guid Id) : IRequest<ValuationRunDto>;

public class GetValuationRunByIdQueryHandler : IRequestHandler<GetValuationRunByIdQuery, ValuationRunDto>
{
    public Task<ValuationRunDto> Handle(GetValuationRunByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
