using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Queries.GetAdviserById;

[Authorize(Policy = Policies.AdvisersManage)]
public record GetAdviserByIdQuery(Guid Id) : IRequest<AdviserDto>;

public class GetAdviserByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAdviserByIdQuery, AdviserDto>
{
    public async Task<AdviserDto> Handle(GetAdviserByIdQuery request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;

        var row = await (
                      from user in db.Users.AsNoTracking()
                      join tenant in db.Tenants.AsNoTracking() on user.TenantId equals tenant.Id
                      where user.PublicId == request.Id
                            && user.Role == UserRole.Adviser
                            && user.TenantId == tenantId
                      select new { user, tenant }
                  ).SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Adviser", request.Id);

        return AdviserDto.From(row.user, row.tenant);
    }
}
