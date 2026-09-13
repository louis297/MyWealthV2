using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Queries.GetTenantAdminById;

[Authorize(Policy = Policies.TenantAdminsManage)]
public record GetTenantAdminByIdQuery(Guid Id) : IRequest<TenantAdminDto>;

public class GetTenantAdminByIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTenantAdminByIdQuery, TenantAdminDto>
{
    public async Task<TenantAdminDto> Handle(GetTenantAdminByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await (
                      from user in db.Users.AsNoTracking()
                      join tenant in db.Tenants.AsNoTracking() on user.TenantId equals tenant.Id
                      where user.PublicId == request.Id && user.Role == UserRole.TenantAdmin
                      select new { user, tenant }
                  ).SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("TenantAdmin", request.Id);

        return TenantAdminDto.From(row.user, row.tenant);
    }
}
