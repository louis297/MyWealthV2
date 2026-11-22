using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Tenants;

public sealed class TenantDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Code { get; init; }

    public required string ReportingCurrency { get; init; }

    public required IReadOnlyList<string> AllowedCustodyModes { get; init; }

    /// <summary>Currencies with an active client money account, A–Z (client-money-accounts R23).</summary>
    public required IReadOnlyList<string> ClientMoneyCurrencies { get; init; }

    /// <summary>The broker connection the Tenant's orders go to; null when it has none (orders R3).</summary>
    public required string? BrokerConnection { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }

    public required DateTimeOffset Created { get; init; }

    public static TenantDto From(Tenant tenant, IReadOnlyList<string> clientMoneyCurrencies) => new()
    {
        Id = tenant.PublicId,
        Name = tenant.Name,
        Code = tenant.Code,
        ReportingCurrency = tenant.ReportingCurrency,
        AllowedCustodyModes = tenant.AllowedCustodyModes.Select(mode => mode.ToString()).ToList(),
        ClientMoneyCurrencies = clientMoneyCurrencies,
        BrokerConnection = tenant.BrokerConnection,
        IsActive = tenant.IsActive,
        RowVersion = Convert.ToBase64String(tenant.RowVersion ?? []),
        Created = tenant.Created
    };
}
