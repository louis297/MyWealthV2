using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using UglyToad.PdfPig;
using static MyWealthV2.Application.UnitTests.Reports.FifReportFixture;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>Fif-report §4.4: the PDF of the content, its sections, its figures and the footer of every page.</summary>
public class CustomerFifPdfTests
{
    [Test]
    public void Render_TheWorkedExample_HoldsTheHeaderTheNineSectionsAndTheFigures()
    {
        var pdf = CustomerFifPdf.Render(Build(Example));

        pdf.Take(4).ShouldBe("%PDF"u8.ToArray());
        var text = string.Join('\n', Pages(pdf));
        foreach (var line in Build(Example).Header)
            text.ShouldContain(line);
        text.ShouldContain("FIF Report");
        foreach (var title in new[]
                 {
                     "1. Summary", "2. About this report", "3. Holdings and classification", "4. Threshold test",
                     "5. FIF income", "6. Each holding", "7. Tax taken from dividends",
                     "8. Prices, rates and values used", "9. What to know about these figures"
                 })
        {
            text.ShouldContain(title);
        }

        foreach (var figure in new[] { "12355.70", "10792.33", "655.42", "1740.87", "13108.48", "13026.08", "2789.60", "117.95", "17.69", "21.23", "52.12884615" })
            text.ShouldContain(figure);
        text.ShouldContain("80 from Old broker (2025-11-10 13:00) to Trading");
        text.ShouldContain("2025-04-01 09:15");
    }

    [Test]
    public void Render_EveryPage_HasTheFooterWithThePageCount()
    {
        var pages = Pages(CustomerFifPdf.Render(Build(Example)));

        for (var page = 0; page < pages.Count; page++)
        {
            pages[page].ShouldContain("Cee Customer");
            pages[page].ShouldContain("FIF Report, tax year 2026");
            pages[page].ShouldContain($"Page {page + 1} of {pages.Count}");
            pages[page].ShouldNotContain("Incomplete");
        }
    }

    [Test]
    public void Render_AnIncompleteReport_SaysIncompleteOnEveryPage()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        year = year with
        {
            Complete = false,
            Holdings = [aaa with { Incomplete = [TaxIncompleteReason.ClosingNotValued] }]
        };

        var pages = Pages(CustomerFifPdf.Render(Build(year)));

        pages.ShouldAllBe(page => page.Contains("Incomplete"));
        pages[^1].ShouldContain($"Page {pages.Count} of {pages.Count}");
    }

    [Test]
    public void Render_AReportOfManyEvents_RunsOverSeveralPages()
    {
        var rows = Enumerable.Range(1, 300)
            .Select(n => (IReadOnlyList<string>)[n.ToString(), "2025-06-01 10:00", "Purchase", "Trading", "+1", n.ToString(), n.ToString(), "10.00", "x 1.70 (2025-06-01)", "+10.00", "100.00"])
            .ToList();
        var content = new CustomerFifContent(
            "FIF Report",
            ["Tenant: Demo Advisory", "Status: Complete"],
            [
                new CustomerFifSection("6. Each holding",
                [
                    new CustomerFifText("AAA Alpha Inc", Heading: true),
                    new CustomerFifTable(
                        ["#", "Time", "Event", "Account", "Quantity", "Quantity after", "Comparable after", "Amount", "Rate", "Cost change", "Tax cost after"],
                        rows)
                ])
            ],
            Complete: true,
            "Cee Customer",
            2026);

        var pages = Pages(CustomerFifPdf.Render(content));

        pages.Count.ShouldBeGreaterThan(3);
        pages[^1].ShouldContain("300");
        pages[^1].ShouldContain($"Page {pages.Count} of {pages.Count}");
    }

    private static List<string> Pages(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(page => string.Join(' ', page.GetWords().Select(word => word.Text))).ToList();
    }
}
