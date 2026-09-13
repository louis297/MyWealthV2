using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Tenants;

public sealed class TenantDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Code { get; init; }

    public required string ReportingCurrency { get; init; }

    public required bool IsEnabled { get; init; }

    public required string RowVersion { get; init; }

    public required DateTimeOffset Created { get; init; }

    public static TenantDto From(Tenant tenant) => new()
    {
        Id = tenant.PublicId,
        Name = tenant.Name,
        Code = tenant.Code,
        ReportingCurrency = tenant.ReportingCurrency,
        IsEnabled = tenant.IsEnabled,
        RowVersion = Convert.ToBase64String(tenant.RowVersion ?? []),
        Created = tenant.Created
    };
}
