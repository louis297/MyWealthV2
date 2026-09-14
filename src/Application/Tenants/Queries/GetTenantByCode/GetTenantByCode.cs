using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Tenants.Queries.GetTenantByCode;

[Authorize(Policy = Policies.TenantsRead)]
public record GetTenantByCodeQuery(string Code) : IRequest<TenantDto>;

public class GetTenantByCodeQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetTenantByCodeQuery, TenantDto>
{
    public async Task<TenantDto> Handle(GetTenantByCodeQuery request, CancellationToken cancellationToken)
    {
        var query = db.Tenants.AsNoTracking().Where(tenant => tenant.Code == request.Code);

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(tenant => tenant.Id == currentUser.TenantId);
        }

        var tenant = await query.SingleOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.Code);

        return TenantDto.From(tenant);
    }
}
