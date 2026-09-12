using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Application.Users.EventHandlers;

public class RevokeTokensOnTenantDisabled(IApplicationDbContext db, ITokenRevocation revocation)
    : INotificationHandler<TenantDisabled>
{
    public async Task Handle(TenantDisabled notification, CancellationToken cancellationToken)
    {
        var subjects = await db.Users.AsNoTracking()
            .Where(user => user.TenantId == notification.Tenant.Id)
            .Select(user => user.PublicId.ToString())
            .ToListAsync(cancellationToken);

        foreach (var subject in subjects)
        {
            await revocation.RevokeSubject(subject, cancellationToken);
        }
    }
}
