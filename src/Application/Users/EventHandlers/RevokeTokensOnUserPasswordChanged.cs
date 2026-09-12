using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Events;

namespace MyWealthV2.Application.Users.EventHandlers;

public class RevokeTokensOnUserPasswordChanged(ITokenRevocation revocation)
    : INotificationHandler<UserPasswordChanged>
{
    public Task Handle(UserPasswordChanged notification, CancellationToken cancellationToken) =>
        revocation.RevokeSubject(notification.User.PublicId.ToString(), cancellationToken);
}
