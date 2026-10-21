using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Infrastructure.Data;

public sealed class TenantIdAccessor : ITenantIdAccessor
{
    public int? TenantId { get; private set; }

    public bool IsEngaged { get; private set; }

    public void Engage(int tenantId)
    {
        TenantId = tenantId;
        IsEngaged = true;
    }
}
