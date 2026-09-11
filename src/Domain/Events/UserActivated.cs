using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class UserActivated : BaseEvent
{
    public UserActivated(User user)
    {
        User = user;
    }

    public User User { get; }
}
