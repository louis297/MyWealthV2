using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;

namespace MyWealthV2.Domain.Entities;

public class Tenant : BaseAuditableEntity
{
    private Tenant()
    {
    }

    public Guid PublicId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string ReportingCurrency { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static Tenant Create(string name, string code, Currency reportingCurrency, Guid? publicId = null)
    {
        EnsureReportingCurrency(reportingCurrency);

        var tenant = new Tenant
        {
            Name = name,
            Code = code,
            PublicId = publicId ?? Guid.NewGuid(),
            IsActive = true,
            ReportingCurrency = reportingCurrency.Code
        };
        tenant.AddDomainEvent(new TenantCreated(tenant));
        return tenant;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required.");
        }

        Name = name;
    }

    public void SetReportingCurrency(Currency currency)
    {
        EnsureReportingCurrency(currency);
        ReportingCurrency = currency.Code;
    }

    public void Enable()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        AddDomainEvent(new TenantEnabled(this));
    }

    public void Disable()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        AddDomainEvent(new TenantDisabled(this));
    }

    private static void EnsureReportingCurrency(Currency currency)
    {
        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            throw new DomainException("Reporting currency is required.");
        }

        if (!currency.IsActive)
        {
            throw new DomainException("Reporting currency must be enabled.");
        }
    }
}
