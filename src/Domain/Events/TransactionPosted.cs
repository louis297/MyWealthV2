using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class TransactionPosted : BaseEvent
{
    public TransactionPosted(Transaction transaction)
    {
        Transaction = transaction;
    }

    public Transaction Transaction { get; }
}
