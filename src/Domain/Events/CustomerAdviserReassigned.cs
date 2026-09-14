namespace MyWealthV2.Domain.Events;

public class CustomerAdviserReassigned : BaseEvent
{
    public CustomerAdviserReassigned(int customerId, int tenantId, int previousAdviserId, int newAdviserId)
    {
        CustomerId = customerId;
        TenantId = tenantId;
        PreviousAdviserId = previousAdviserId;
        NewAdviserId = newAdviserId;
    }

    public int CustomerId { get; }

    public int TenantId { get; }

    public int PreviousAdviserId { get; }

    public int NewAdviserId { get; }
}
