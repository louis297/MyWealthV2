using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class UserCreated : BaseEvent
{
    public UserCreated(User user)
    {
        User = user;
    }

    public User User { get; }
}
