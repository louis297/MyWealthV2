using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Tenants.Queries.GetTenantById;

[Authorize(Policy = Policies.TenantsManage)]
public record GetTenantByIdQuery(Guid Id) : IRequest<TenantDto>;

public class GetTenantByIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTenantByIdQuery, TenantDto>
{
    public async Task<TenantDto> Handle(GetTenantByIdQuery request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.Id);

        return TenantDto.From(tenant);
    }
}
