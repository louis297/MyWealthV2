using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Application.Users.EventHandlers;

public class RevokeTokensOnUserDisabled(IServiceScopeFactory scopes) : INotificationHandler<UserDisabled>
{
    public async Task Handle(UserDisabled notification, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var revocation = scope.ServiceProvider.GetRequiredService<ITokenRevocation>();
        await revocation.RevokeSubject(notification.User.PublicId.ToString(), cancellationToken);
    }
}
