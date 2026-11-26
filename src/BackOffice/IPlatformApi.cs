namespace MyWealthV2.BackOffice;

public interface IPlatformApi
{
    Task<TenantPage> ListTenantsAsync(int page, int pageSize, bool? isEnabled, string? search, CancellationToken cancellationToken = default);

    Task<TenantItem> GetTenantAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Orders BO26: <paramref name="brokerConnection"/> is a configured connection, or null for none.</summary>
    Task<Guid> CreateTenantAsync(string name, string code, string reportingCurrency, IReadOnlyList<string> allowedCustodyModes, string? brokerConnection = null, CancellationToken cancellationToken = default);

    /// <summary>Orders BO26: a null <paramref name="brokerConnection"/> leaves the tenant's connection as it is.</summary>
    Task UpdateTenantAsync(Guid id, string name, string reportingCurrency, IReadOnlyList<string> allowedCustodyModes, string rowVersion, string? brokerConnection = null, CancellationToken cancellationToken = default);

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

    Task<IReadOnlyList<PlatformReportItem>> ListReportCatalogAsync(CancellationToken cancellationToken = default);

    Task UpdateReportCatalogAsync(
        Guid reportId,
        bool? isActive,
        IReadOnlyList<string>? allowedTriggers,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientMoneyAccountItem>> ListClientMoneyAccountsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<ClientMoneyAccountItem> GetClientMoneyAccountAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateClientMoneyAccountAsync(
        Guid tenantId,
        string currency,
        string bankName,
        string accountName,
        string accountNumber,
        string bankConnection,
        CancellationToken cancellationToken = default);

    /// <summary>In-kind transfers BO30: the tenant's safekeeping accounts with their feed.</summary>
    Task<IReadOnlyList<SafekeepingAccountItem>> ListSafekeepingAccountsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Guid> CreateSafekeepingAccountAsync(
        Guid tenantId,
        string currency,
        string custodianName,
        string accountName,
        string accountNumber,
        string custodianConnection,
        CancellationToken cancellationToken = default);

    Task DisableSafekeepingAccountAsync(Guid tenantId, Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task EnableSafekeepingAccountAsync(Guid tenantId, Guid id, string rowVersion, CancellationToken cancellationToken = default);

    /// <summary>In-kind transfers BO31: the configured custodian connections.</summary>
    Task<IReadOnlyList<CustodianConnectionItem>> ListCustodianConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The configured bank connections (deposit-matching R3).</summary>
    Task<IReadOnlyList<BankConnectionItem>> ListBankConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The configured market data connections with their price feed state (simulated-instruments R5).</summary>
    Task<IReadOnlyList<MarketDataConnectionItem>> ListMarketDataConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Orders BO27: the configured broker connections with the state of their report feed.</summary>
    Task<IReadOnlyList<BrokerConnectionItem>> ListBrokerConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Orders BO28: the newest twenty execution reports, optionally of one status.</summary>
    Task<IReadOnlyList<ExecutionReportItem>> ListExecutionReportsAsync(string? status, CancellationToken cancellationToken = default);

    /// <summary>Corporate actions BO25: the newest twenty announcements, optionally of one status.</summary>
    Task<IReadOnlyList<CorporateActionAnnouncementItem>> ListCorporateActionAnnouncementsAsync(
        string? status,
        CancellationToken cancellationToken = default);

    Task DisableClientMoneyAccountAsync(Guid tenantId, Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task EnableClientMoneyAccountAsync(Guid tenantId, Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurrencyItem>> ListCurrenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>The tenant's newest ten interest runs (interest-run BO20).</summary>
    Task<IReadOnlyList<InterestRunItem>> ListInterestRunsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Runs interest for the tenant and an as-of date at once (interest-run R32, BO21).</summary>
    Task<Guid> StartInterestRunAsync(Guid tenantId, DateOnly asOfDate, CancellationToken cancellationToken = default);

    /// <summary>The tenant's newest ten valuation runs (valuation-run BO34).</summary>
    Task<IReadOnlyList<ValuationRunItem>> ListValuationRunsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Runs the valuations of the tenant through a date at once (valuation-run R15, BO35).</summary>
    Task<Guid> StartValuationRunAsync(Guid tenantId, DateOnly throughDate, CancellationToken cancellationToken = default);

    Task<InstrumentPage> ListInstrumentsAsync(
        int page,
        int pageSize,
        Guid tenantId,
        bool? enabledOnly,
        string? search,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateInstrumentAsync(
        Guid tenantId,
        string symbol,
        string name,
        string quoteCurrency,
        CancellationToken cancellationToken = default);

    Task<InstrumentItem> GetInstrumentAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpdateInstrumentAsync(
        Guid id,
        string symbol,
        string name,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task DisableInstrumentAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);

    Task EnableInstrumentAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);
}
