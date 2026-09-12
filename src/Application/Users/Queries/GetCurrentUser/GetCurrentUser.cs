using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Application.Users.Queries.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<CurrentUserDto>;

public class GetCurrentUserQueryHandler(ICurrentUser currentUser, IApplicationDbContext db)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var person = currentUser.Person
            ?? throw new UnauthorizedAccessException();

        Domain.Entities.Tenant? tenant = null;
        if (person.TenantId is int tenantId)
        {
            tenant = await db.Tenants.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken);
        }

        Domain.Entities.User? adviser = null;
        if (person.AdviserId is int adviserId)
        {
            adviser = await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adviserId, cancellationToken);
        }

        return CurrentUserDto.From(person, tenant, adviser);
    }
}
