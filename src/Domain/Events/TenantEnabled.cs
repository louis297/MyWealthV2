using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class TenantEnabled : BaseEvent
{
    public TenantEnabled(Tenant tenant)
    {
        Tenant = tenant;
    }

    public Tenant Tenant { get; }
}
