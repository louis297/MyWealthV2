using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccounts;

[Authorize(Policy = Policies.TenantsManage)]
public record GetClientMoneyAccountsQuery(
    Guid TenantId,
    int Page = 1,
    int PageSize = 20,
    string? EnabledOnly = null) : IRequest<PagedList<ClientMoneyAccountDto>>;

public class GetClientMoneyAccountsQueryHandler
    : IRequestHandler<GetClientMoneyAccountsQuery, PagedList<ClientMoneyAccountDto>>
{
    public Task<PagedList<ClientMoneyAccountDto>> Handle(
        GetClientMoneyAccountsQuery request,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
