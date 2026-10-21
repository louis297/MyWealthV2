namespace MyWealthV2.Application.Common.Interfaces;

public interface ITenantIdAccessor
{
    int? TenantId { get; }

    bool IsEngaged { get; }

    void Engage(int tenantId);
}
