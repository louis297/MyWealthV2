using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Application.Users.EventHandlers;

public class RevokeTokensOnUserPasswordChanged(IServiceScopeFactory scopes)
    : INotificationHandler<UserPasswordChanged>
{
    public async Task Handle(UserPasswordChanged notification, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var revocation = scope.ServiceProvider.GetRequiredService<ITokenRevocation>();
        await revocation.RevokeSubject(notification.User.PublicId.ToString(), cancellationToken);
    }
}
