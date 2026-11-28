using MyWealthV2.Application.TaxYears;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The content of the Tax Year Income Summary from one tax year read (docs/features/income-summary.md §4.4 to §4.6).
/// No I/O and no arithmetic: every figure is a figure of <c>interest</c>, of <c>income</c> or of a <c>Dividend</c>
/// event of the read. The one place the words of the report are chosen.
/// </summary>
public static class CustomerIncomeSummaryReport
{
    public const string Title = "Tax Year Income Summary";

    public static CustomerIncomeSummaryContent Build(
        CustomerTaxLedgerRead read,
        CustomerIncomeSummaryNames names,
        TaxResidency residency,
        DateTimeOffset generatedAt,
        DateTimeOffset ledgerReadAt) =>
        new(Title, [], [], false, names.CustomerName, read.Year.TaxYear);
}
