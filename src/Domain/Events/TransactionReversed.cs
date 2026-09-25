using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class TransactionReversed : BaseEvent
{
    public TransactionReversed(Transaction transaction)
    {
        Transaction = transaction;
    }

    public Transaction Transaction { get; }
}
