using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;
using UglyToad.PdfPig;
using static MyWealthV2.Application.UnitTests.Reports.IncomeSummaryFixture;
using static MyWealthV2.Domain.UnitTests.Tax.IncomeWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>Income-summary §4.4: the PDF of the content, its sections, its figures and the footer of every page.</summary>
public class CustomerIncomeSummaryPdfTests
{
    [Test]
    public void Render_TheWorkedExample_HoldsTheHeaderTheSevenSectionsAndTheFigures()
    {
        var content = Build(Example);

        var pdf = CustomerIncomeSummaryPdf.Render(content);

        pdf.Take(4).ShouldBe("%PDF"u8.ToArray());
        var text = string.Join('\n', Pages(pdf));
        text.ShouldContain("Tax Year Income Summary");
        foreach (var line in content.Header)
            text.ShouldContain(line);
        foreach (var title in new[]
                 {
                     "1. Summary", "2. About this report", "3. Dividends", "4. Interest", "5. Each payment", "6. Exchange rates used",
                     "7. What to know about these figures"
                 })
        {
            text.ShouldContain(title);
        }

        foreach (var figure in new[] { "1164.95", "1444.95", "280.00", "66.74", "71.23", "133.70", "25.51", "96.74", "49.05", "7.05", "2.32" })
            text.ShouldContain(figure);
        text.ShouldContain("Kiwi Power Ltd");
        text.ShouldContain("x 1.72 (2026-01-30)");
        text.ShouldContain("entered by hand");
    }

    [Test]
    public void Render_EveryPage_HasTheFooterWithThePageCount()
    {
        var pdf = CustomerIncomeSummaryPdf.Render(Build(Example));

        pdf.Take(4).ShouldBe("%PDF"u8.ToArray());
        var pages = Pages(pdf);
        for (var page = 0; page < pages.Count; page++)
        {
            pages[page].ShouldContain("Cee Customer");
            pages[page].ShouldContain("Tax Year Income Summary, tax year 2026");
            pages[page].ShouldContain($"Page {page + 1} of {pages.Count}");
            pages[page].ShouldNotContain("Incomplete");
        }
    }

    [Test]
    public void Render_AnIncompleteReportOfManyPayments_SaysIncompleteOnEveryPage()
    {
        var postings = Enumerable.Range(1, 200)
            .Select(n => Posting(1_000 + n, Utc(2025, 6, 1, 0, 0).AddHours(n), 1.00m + n, 0.33m, null))
            .ToList();

        var pdf = CustomerIncomeSummaryPdf.Render(Build(Year(postings, rates: [])));

        pdf.Take(4).ShouldBe("%PDF"u8.ToArray());
        var pages = Pages(pdf);
        pages.Count.ShouldBeGreaterThan(3);
        pages.ShouldAllBe(page => page.Contains("Incomplete"));
        pages[^1].ShouldContain($"Page {pages.Count} of {pages.Count}");
    }

    private static List<string> Pages(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(page => string.Join(' ', page.GetWords().Select(word => word.Text))).ToList();
    }
}
