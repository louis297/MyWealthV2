using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccountById;

[Authorize(Policy = Policies.TenantsManage)]
public record GetClientMoneyAccountByIdQuery(Guid TenantId, Guid Id) : IRequest<ClientMoneyAccountDto>;

public class GetClientMoneyAccountByIdQueryHandler
    : IRequestHandler<GetClientMoneyAccountByIdQuery, ClientMoneyAccountDto>
{
    public Task<ClientMoneyAccountDto> Handle(GetClientMoneyAccountByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
