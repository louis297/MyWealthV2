using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace MyWealthV2.Application.Reports;

/// <summary>The PDF of a FIF report (docs/features/fif-report.md §4.4). Layout only.</summary>
public static class CustomerFifPdf
{
    public static byte[] Render(CustomerFifContent content)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container => container.Page(page => page.Content().Text(""))).GeneratePdf();
    }
}
