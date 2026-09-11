using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class UserPasswordChanged : BaseEvent
{
    public UserPasswordChanged(User user)
    {
        User = user;
    }

    public User User { get; }
}
