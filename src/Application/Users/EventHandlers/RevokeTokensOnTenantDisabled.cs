using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Application.Users.EventHandlers;

public class RevokeTokensOnTenantDisabled(IServiceScopeFactory scopes) : INotificationHandler<TenantDisabled>
{
    public async Task Handle(TenantDisabled notification, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var revocation = scope.ServiceProvider.GetRequiredService<ITokenRevocation>();
        scope.ServiceProvider.GetRequiredService<ITenantIdAccessor>().Engage(notification.Tenant.Id);

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
