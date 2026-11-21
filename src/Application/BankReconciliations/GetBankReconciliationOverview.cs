using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>R19: one entry per client money account of the tenant that is active or has a run.</summary>
[Authorize(Policy = Policies.BankReconciliationsManage)]
public sealed record GetBankReconciliationOverviewQuery(Guid? TenantId = null) : IRequest<PagedList<BankReconciliationOverviewDto>>;

public sealed class GetBankReconciliationOverviewQueryHandler
    : IRequestHandler<GetBankReconciliationOverviewQuery, PagedList<BankReconciliationOverviewDto>>
{
    public Task<PagedList<BankReconciliationOverviewDto>> Handle(
        GetBankReconciliationOverviewQuery request,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
