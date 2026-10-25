namespace MyWealthV2.BackOffice;

public sealed record TenantItem(
    Guid Id,
    string Name,
    string Code,
    string ReportingCurrency,
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
    bool IsGranted);

public sealed record CurrencyItem(string Code, string Name, bool IsActive);

public sealed class PlatformApiException(int status, string body) : Exception(body)
{
    public int Status { get; } = status;
}
