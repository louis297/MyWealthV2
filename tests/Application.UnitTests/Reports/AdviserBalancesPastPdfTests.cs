using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>Valuation-history R18, R19: the PDF of the Adviser Balances Report for a past date.</summary>
public class AdviserBalancesPastPdfTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 3, 13, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void Render_TheWorkedExample_ShowsTheDayThePriceAndItsDateAndTheStatus_AndNoCost()
    {
        var text = Read(AdviserBalancesPdf.RenderPast(Model(
            [
                new AdviserBalancesPdfCashRow("A", "A1", "Brokerage", "Custodial", "14356.60 NZD"),
                new AdviserBalancesPdfPastHoldingRow("A", "A1", "Custodial", "AAA", "1500", "2.61 (2026-03-11)", "3915.00 NZD", ""),
                new AdviserBalancesPdfPastHoldingRow("A", "A1", "Custodial", "CCC", "120", "41.30 (2026-03-06)", "", "Price too old")
            ],
            [
                new AdviserBalancesPdfSubtotal("NZD", "14356.60 NZD", "3915.00 NZD"),
                new AdviserBalancesPdfSubtotal("USD", "0.00 USD", "9520.00 USD")
            ],
            holdingsNotValued: 1,
            accountsNotValuedYet: 0)));

        text.ShouldContain("Adviser Balances Report");
        text.ShouldContain("Balances at the end of 2026-03-11 (UTC)");
        text.ShouldContain("As of: 2026-03-13 10:00:00 UTC");
        text.ShouldContain("Incomplete: 1 holdings not valued, 0 accounts not valued yet. The subtotals leave them out.");
        text.ShouldContain("Price");
        text.ShouldContain("Status");
        text.ShouldNotContain("Cost");
        text.ShouldNotContain("Include closed accounts");
        text.ShouldContain("2.61 (2026-03-11)");
        text.ShouldContain("41.30 (2026-03-06)");
        text.ShouldContain("Price too old");
        text.ShouldContain("NZD cash 14356.60 NZD market value 3915.00 NZD");
        text.ShouldContain("USD cash 0.00 USD market value 9520.00 USD");
        var subtotals = text.IndexOf("All accounts", StringComparison.Ordinal);
        var note = text.IndexOf(
            "Custodial accounts: the daily valuation of that day. External accounts: the ledger as it stood when this report was made.",
            StringComparison.Ordinal);
        subtotals.ShouldBeGreaterThanOrEqualTo(0);
        note.ShouldBeGreaterThan(subtotals);
    }

    [Test]
    public void Render_AnAccountNotValuedYet_IsALine_AndCountedInTheHeader()
    {
        var text = Read(AdviserBalancesPdf.RenderPast(Model(
            [new AdviserBalancesPdfNotValuedRow("B", "B1", "Brokerage", "Custodial")],
            [],
            holdingsNotValued: 0,
            accountsNotValuedYet: 1)));

        text.ShouldContain("B1");
        text.ShouldContain("Not valued yet");
        text.ShouldContain("Incomplete: 0 holdings not valued, 1 accounts not valued yet.");
    }

    [Test]
    public void Render_ACompleteReport_HasNoIncompleteLine_AndNoBalancesWhenEmpty()
    {
        var text = Read(AdviserBalancesPdf.RenderPast(Model([], [], 0, 0)));

        text.ShouldContain("Balances at the end of 2026-03-11 (UTC)");
        text.ShouldContain("No balances.");
        text.ShouldNotContain("Incomplete");
    }

    private static AdviserBalancesPastPdfModel Model(
        IReadOnlyList<AdviserBalancesPdfRow> rows,
        IReadOnlyList<AdviserBalancesPdfSubtotal> subtotals,
        int holdingsNotValued,
        int accountsNotValuedYet) =>
        new("Firm Book", "Ana", new DateOnly(2026, 3, 11), AsOf, AsOf.AddSeconds(5), rows, subtotals,
            subtotals.Count == 0 ? [] : [new AdviserBalancesPdfSubtotalBlock("Custodial", subtotals)],
            holdingsNotValued, accountsNotValuedYet);

    private static string Read(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }
}
