using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Events;

namespace MyWealthV2.Application.Users.EventHandlers;

public class RevokeTokensOnUserDisabled(ITokenRevocation revocation) : INotificationHandler<UserDisabled>
{
    public Task Handle(UserDisabled notification, CancellationToken cancellationToken) =>
        revocation.RevokeSubject(notification.User.PublicId.ToString(), cancellationToken);
}
