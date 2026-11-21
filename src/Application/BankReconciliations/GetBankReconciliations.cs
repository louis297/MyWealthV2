using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>R20: the current tenant's runs, newest first.</summary>
[Authorize(Policy = Policies.BankReconciliationsManage)]
public sealed record GetBankReconciliationsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ClientMoneyAccountId = null,
    string? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? TenantId = null) : IRequest<PagedList<BankReconciliationListItemDto>>;

public sealed class GetBankReconciliationsQueryHandler
    : IRequestHandler<GetBankReconciliationsQuery, PagedList<BankReconciliationListItemDto>>
{
    public Task<PagedList<BankReconciliationListItemDto>> Handle(
        GetBankReconciliationsQuery request,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
