using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class TenantDisabled : BaseEvent
{
    public TenantDisabled(Tenant tenant)
    {
        Tenant = tenant;
    }

    public Tenant Tenant { get; }
}
