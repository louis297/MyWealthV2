namespace MyWealthV2.BackOffice;

public interface IPlatformApi
{
    Task<TenantPage> ListTenantsAsync(int page, int pageSize, bool? isEnabled, string? search, CancellationToken cancellationToken = default);

    Task<TenantItem> GetTenantAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> CreateTenantAsync(string name, string code, string reportingCurrency, CancellationToken cancellationToken = default);

    Task UpdateTenantAsync(Guid id, string name, string reportingCurrency, string rowVersion, CancellationToken cancellationToken = default);

    Task DisableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task EnableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task<TenantAdminPage> ListTenantAdminsAsync(int page, int pageSize, Guid? tenantId, bool? enabledOnly, string? search, CancellationToken cancellationToken = default);

    Task<TenantAdminItem> GetTenantAdminAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> CreateTenantAdminAsync(Guid tenantId, string name, string email, string password, CancellationToken cancellationToken = default);

    Task UpdateTenantAdminAsync(Guid id, string name, string rowVersion, CancellationToken cancellationToken = default);

    Task DisableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task EnableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportGrantItem>> ListReportGrantsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task GrantReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default);

    Task RevokeReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurrencyItem>> ListCurrenciesAsync(CancellationToken cancellationToken = default);
}
