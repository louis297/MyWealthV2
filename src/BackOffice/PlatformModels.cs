namespace MyWealthV2.BackOffice;

public sealed record TenantItem(
    Guid Id,
    string Name,
    string Code,
    string ReportingCurrency,
    IReadOnlyList<string> AllowedCustodyModes,
    bool IsActive,
    string RowVersion,
    string? BrokerConnection = null);

public sealed record TenantPage(
    IReadOnlyList<TenantItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record TenantAdminItem(
    Guid Id,
    Guid TenantId,
    string Name,
    string Email,
    string Status,
    bool IsActive,
    string RowVersion);

public sealed record TenantAdminPage(
    IReadOnlyList<TenantAdminItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ReportGrantItem(
    Guid Id,
    string Code,
    string Name,
    string Scope,
    bool IsActive,
    bool Granted);

public sealed record PlatformReportItem(
    Guid Id,
    string Code,
    string Name,
    string Scope,
    IReadOnlyList<string> AllowedTriggers,
    bool IsActive);

public sealed record CurrencyItem(string Code, string Name, bool IsActive);

public sealed record InstrumentItem(
    Guid Id,
    Guid TenantId,
    string Symbol,
    string Name,
    string QuoteCurrency,
    bool IsActive,
    string RowVersion,
    string? MarketDataConnection = null);

public sealed record InstrumentPage(
    IReadOnlyList<InstrumentItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>A tenant's client money account (docs/features/client-money-accounts.md §8.1).</summary>
public sealed record ClientMoneyAccountItem(
    Guid Id,
    Guid TenantId,
    string Currency,
    string BankName,
    string AccountName,
    string AccountNumber,
    bool IsActive,
    string RowVersion,
    DateTimeOffset Created,
    string BankConnection = "",
    ClientMoneyAccountFeed? BankFeed = null);

/// <summary>How the last read of a client money account's statement went (docs/features/deposit-matching.md R4).</summary>
public sealed record ClientMoneyAccountFeed(DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, string? LastFailure);

/// <summary>A tenant's safekeeping account at a custodian (docs/features/in-kind-transfers.md R7).</summary>
public sealed record SafekeepingAccountItem(
    Guid Id,
    string Currency,
    string CustodianName,
    string AccountName,
    string AccountNumber,
    string CustodianConnection,
    bool IsActive,
    string RowVersion,
    CustodianFeedState? CustodianFeed = null);

/// <summary>How the last read of a safekeeping account's custodian feed went, and when its positions are from (R7).</summary>
public sealed record CustodianFeedState(
    DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, string? LastFailure, DateTimeOffset? PositionsAt);

public sealed record SafekeepingAccountPage(IReadOnlyList<SafekeepingAccountItem> Items, int Page, int PageSize, int TotalCount);

/// <summary>A configured custodian connection (in-kind-transfers R1).</summary>
public sealed record CustodianConnectionItem(string Name, string Adapter);

public sealed record CustodianConnectionPage(IReadOnlyList<CustodianConnectionItem> Items, int Page, int PageSize, int TotalCount);

/// <summary>A configured bank connection (deposit-matching R3).</summary>
public sealed record BankConnectionItem(string Name, string Adapter);

public sealed record BankConnectionPage(IReadOnlyList<BankConnectionItem> Items, int Page, int PageSize, int TotalCount);

/// <summary>How the last read of a market data feed went (docs/features/simulated-instruments.md R5).</summary>
public sealed record MarketDataFeed(DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, string? LastFailure);

/// <summary>A configured market data connection and the state of its price feed (simulated-instruments R5) and its
/// corporate action feed (corporate-actions R38).</summary>
public sealed record MarketDataConnectionItem(
    string Name,
    string Adapter,
    string SymbolSuffix,
    MarketDataFeed? Prices,
    MarketDataFeed? CorporateActions);

/// <summary>A stored corporate action announcement (corporate-actions R14).</summary>
public sealed record CorporateActionAnnouncementItem(
    Guid Id,
    string Connection,
    string OutsideId,
    string PartyActionId,
    int Version,
    string PartyStatus,
    string Symbol,
    string Currency,
    string? Kind,
    int RatioNew,
    int RatioHeld,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate,
    DateTimeOffset ReceivedAt,
    string Status,
    string? SkipReason,
    DateTimeOffset? ProcessedAt);

public sealed record CorporateActionAnnouncementPage(IReadOnlyList<CorporateActionAnnouncementItem> Items, int Page, int PageSize, int TotalCount);

/// <summary>The state of a broker connection's report feed (orders R4).</summary>
public sealed record BrokerFeedState(DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, string? LastFailure);

/// <summary>A configured broker connection (orders R4).</summary>
public sealed record BrokerConnectionItem(
    string Name,
    string Adapter,
    BrokerFeedState? Reports,
    IReadOnlyList<string>? SettlementCurrencies = null);

public sealed record BrokerConnectionPage(IReadOnlyList<BrokerConnectionItem> Items, int Page, int PageSize, int TotalCount);

/// <summary>A stored execution report (orders R30). <see cref="Kind"/> is null for a kind the adapter did not know.</summary>
public sealed record ExecutionReportItem(
    Guid Id,
    string Connection,
    string OutsideId,
    string? Kind,
    string? ClientOrderId,
    string? BrokerOrderId,
    decimal? Quantity,
    decimal? Price,
    DateOnly? TradeDate,
    DateOnly? SettlementDate,
    string? ContractNote,
    string? FillOutsideId,
    DateOnly? SettledDate,
    string? Reason,
    DateTimeOffset ReceivedAt,
    string Status,
    string? SkipReason,
    DateTimeOffset? ProcessedAt,
    string? SettlementId = null);

public sealed record ExecutionReportPage(IReadOnlyList<ExecutionReportItem> Items, int Page, int PageSize, int TotalCount);

public sealed record MarketDataConnectionPage(IReadOnlyList<MarketDataConnectionItem> Items, int Page, int PageSize, int TotalCount);

public sealed record ClientMoneyAccountPage(
    IReadOnlyList<ClientMoneyAccountItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>An interest run of a tenant (docs/features/interest-run.md R37).</summary>
public sealed record InterestRunItem(
    Guid Id,
    DateOnly AsOfDate,
    DateOnly PostingDate,
    string Trigger,
    int AccountsProcessed,
    int AccountsFailed,
    int PostingCount,
    IReadOnlyList<InterestRunTotal> Totals);

public sealed record InterestRunTotal(string Currency, int PostingCount, decimal GrossAmount, decimal TaxAmount);

public sealed record InterestRunPage(IReadOnlyList<InterestRunItem> Items, int Page, int PageSize, int TotalCount);

public sealed class PlatformApiException(int status, string body) : Exception(body)
{
    public int Status { get; } = status;
}
