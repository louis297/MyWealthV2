using System.Text;
using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.UnitTests.Reports;

public class CustomerTransactionsPdfTests
{
    [Test]
    public void Render_EmptyRange_StatesThereAreNoRows()
    {
        var pdf = CustomerTransactionsPdf.Render(new CustomerTransactionsPdfModel(
            "Firm Queue",
            new DateTimeOffset(2026, 3, 31, 14, 5, 6, TimeSpan.Zero),
            "Cee",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31),
            IncludeClosedAccounts: false,
            []));

        pdf.Length.ShouldBeGreaterThan(4);
        Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");
        var text = Read(pdf);
        text.ShouldContain("Customer Transactions Report");
        text.ShouldContain("Tenant: Firm Queue");
        text.ShouldContain("Generated: 2026-03-31 14:05:06 UTC");
        text.ShouldContain("Customer: Cee");
        text.ShouldContain("From: 2026-01-01");
        text.ShouldContain("To: 2026-03-31");
        text.ShouldContain("Include closed accounts: no");
        text.ShouldContain("No transactions in this range.");
    }

    internal static string Read(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }
}
