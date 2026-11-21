using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The report scopes and the coarse policies each one selects (reports R4–R6; bank-reconciliation R22): a report about
/// one Customer, about an Adviser's book, or about the whole Tenant.
/// </summary>
public static class ReportScopes
{
    public const string Customer = "Customer";
    public const string Book = "Book";
    public const string Tenant = "Tenant";

    public const string InvalidMessage = "Scope must be Customer, Book or Tenant.";

    public static IReadOnlyList<string> All { get; } = [Customer, Book, Tenant];

    public static bool IsKnown(string scope) => All.Contains(scope, StringComparer.Ordinal);

    public static string ReadPolicy(string scope) => scope switch
    {
        Book => Policies.ReportsBookRead,
        Tenant => Policies.ReportsTenantRead,
        _ => Policies.ReportsCustomerRead
    };

    public static string CreatePolicy(string scope) => scope switch
    {
        Book => Policies.ReportsBookCreate,
        Tenant => Policies.ReportsTenantCreate,
        _ => Policies.ReportsCustomerCreate
    };
}
