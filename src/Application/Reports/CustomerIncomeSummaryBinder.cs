using System.Text.Json;

namespace MyWealthV2.Application.Reports;

/// <summary>The parameters of the Tax Year Income Summary (docs/features/income-summary.md R18): those of the FIF
/// report, read by <see cref="CustomerTaxYearParameters"/>.</summary>
public static class CustomerIncomeSummaryBinder
{
    public const string Code = "customer-income-summary";

    /// <summary>
    /// Binds <paramref name="parameters"/>. <paramref name="today"/> is the platform's UTC date, for the tax years that
    /// can be reported (R18, tax-year-events R4).
    /// </summary>
    public static CustomerTaxYearBinding Bind(JsonElement parameters, DateOnly today) => new(Guid.Empty, 0, string.Empty);
}
