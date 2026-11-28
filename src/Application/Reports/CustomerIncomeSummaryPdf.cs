using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The PDF of a Tax Year Income Summary (docs/features/income-summary.md §4.4): A4 landscape, the header, the numbered
/// sections, and a footer on every page with the Customer's name, the report and tax year, the page count and, when the
/// report is not complete, "Incomplete". Layout only: every word comes from <see cref="CustomerIncomeSummaryReport.Build"/>.
/// The layout is that of <see cref="CustomerFifPdf"/>.
/// </summary>
public static class CustomerIncomeSummaryPdf
{
    public static byte[] Render(CustomerIncomeSummaryContent content)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontSize(8.5f));
                page.Content().Column(column =>
                {
                    column.Spacing(3);
                    column.Item().Text(content.Title).Bold().FontSize(16);
                    foreach (var line in content.Header)
                        column.Item().Text(line);

                    foreach (var section in content.Sections)
                    {
                        column.Item().PaddingTop(12).Text(section.Title).Bold().FontSize(12);
                        foreach (var block in section.Blocks)
                            Block(column, block);
                    }
                });
                page.Footer().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(content.CustomerName);
                    row.RelativeItem().AlignCenter().Text(content.FooterTitle);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                        if (!content.Complete)
                            text.Span("   Incomplete").Bold();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Block(ColumnDescriptor column, IncomeSummaryBlock block)
    {
        switch (block)
        {
            case IncomeSummaryText { Heading: true } heading:
                column.Item().PaddingTop(6).Text(heading.Text).Bold().FontSize(10);
                break;
            case IncomeSummaryText text:
                column.Item().Text(text.Text);
                break;
            case IncomeSummaryTable table:
                column.Item().PaddingVertical(2).Table(layout =>
                {
                    layout.ColumnsDefinition(columns =>
                    {
                        foreach (var width in Widths(table))
                            columns.RelativeColumn(width);
                    });
                    layout.Header(header =>
                    {
                        foreach (var title in table.Columns)
                            header.Cell().BorderBottom(0.5f).PaddingBottom(2).Text(title).Bold();
                    });
                    foreach (var row in table.Rows)
                    {
                        for (var cell = 0; cell < table.Columns.Count; cell++)
                            layout.Cell().PaddingVertical(1).PaddingRight(4).Text(cell < row.Count ? row[cell] : "");
                    }
                });
                break;
        }
    }

    /// <summary>A column is as wide as its longest cell, within bounds, so that a long amount keeps to one line.</summary>
    private static IEnumerable<float> Widths(IncomeSummaryTable table) =>
        table.Columns.Select((title, index) =>
        {
            var longest = table.Rows.Select(row => index < row.Count ? row[index].Length : 0).DefaultIfEmpty(0).Max();
            var titleWords = title.Split(' ').Max(word => word.Length);
            return (float)Math.Clamp(Math.Max(longest, titleWords), 3, 50);
        });
}
