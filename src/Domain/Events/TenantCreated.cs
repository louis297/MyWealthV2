using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class TenantCreated : BaseEvent
{
    public TenantCreated(Tenant tenant)
    {
        Tenant = tenant;
    }

    public Tenant Tenant { get; }
}
