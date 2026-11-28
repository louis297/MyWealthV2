using MyWealthV2.Application.TaxYears;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The content of the FIF report from one tax year read (docs/features/fif-report.md §4.3 to §4.7). No I/O. The one
/// place the words of the report are chosen.
/// </summary>
public static partial class CustomerFifReport
{
    public static CustomerFifContent Build(
        CustomerTaxLedgerRead read,
        CustomerFifNames names,
        TaxResidency residency,
        DateTimeOffset generatedAt,
        DateTimeOffset ledgerReadAt) =>
        new("FIF Report", [], [], false, names.CustomerName, read.Year.TaxYear);
}
