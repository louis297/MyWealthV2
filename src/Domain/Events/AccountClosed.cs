using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class AccountClosed : BaseEvent
{
    public AccountClosed(Account account)
    {
        Account = account;
    }

    public Account Account { get; }
}
