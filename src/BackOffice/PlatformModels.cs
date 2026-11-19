namespace MyWealthV2.BackOffice;

public sealed record TenantItem(
    Guid Id,
    string Name,
    string Code,
    string ReportingCurrency,
    IReadOnlyList<string> AllowedCustodyModes,
    bool IsActive,
    string RowVersion);

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
    string RowVersion);

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
    DateTimeOffset Created);

public sealed record ClientMoneyAccountPage(
    IReadOnlyList<ClientMoneyAccountItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed class PlatformApiException(int status, string body) : Exception(body)
{
    public int Status { get; } = status;
}
