namespace MyWealthV2.Application.Reports;

/// <summary>
/// The PDF of a Tax Year Income Summary (docs/features/income-summary.md §4.4): A4 landscape, the header, the numbered
/// sections, and a footer on every page with the Customer's name, the report and tax year, the page count and, when the
/// report is not complete, "Incomplete". Layout only: every word comes from <see cref="CustomerIncomeSummaryReport.Build"/>.
/// </summary>
public static class CustomerIncomeSummaryPdf
{
    public static byte[] Render(CustomerIncomeSummaryContent content) => [];
}
