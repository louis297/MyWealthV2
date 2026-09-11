using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class UserDisabled : BaseEvent
{
    public UserDisabled(User user)
    {
        User = user;
    }

    public User User { get; }
}
