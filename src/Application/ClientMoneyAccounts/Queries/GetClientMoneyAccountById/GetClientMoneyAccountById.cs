using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.ClientMoneyAccounts.Queries.GetClientMoneyAccountById;

[Authorize(Policy = Policies.TenantsManage)]
public record GetClientMoneyAccountByIdQuery(Guid TenantId, Guid Id) : IRequest<ClientMoneyAccountDto>;

public class GetClientMoneyAccountByIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetClientMoneyAccountByIdQuery, ClientMoneyAccountDto>
{
    public async Task<ClientMoneyAccountDto> Handle(GetClientMoneyAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var tenant = await ClientMoneyAccountLookup.TenantAsync(db, request.TenantId, cancellationToken);
        var row = await db.ClientMoneyAccounts.AsNoTracking()
                      .SingleOrDefaultAsync(r => r.TenantId == tenant.Id && r.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Client money account", request.Id);
        return ClientMoneyAccountDto.From(row, tenant);
    }
}
