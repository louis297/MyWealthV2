using System.Text;
using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.UnitTests.Reports;

public class AdviserBalancesPdfTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset GeneratedAt = new(2026, 10, 16, 8, 31, 5, TimeSpan.Zero);

    [Test]
    public void Render_EmptyBook_StatesThereAreNoBalances()
    {
        var pdf = AdviserBalancesPdf.Render(Model([], []));

        pdf.Length.ShouldBeGreaterThan(4);
        Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");
        var text = Read(pdf);
        text.ShouldContain("Adviser Balances Report");
        text.ShouldContain("Tenant: Firm Book");
        text.ShouldContain("Adviser: Ann");
        text.ShouldContain("As of: 2026-10-16 08:30:00 UTC");
        text.ShouldContain("Generated: 2026-10-16 08:31:05 UTC");
        text.ShouldContain("Include closed accounts: no");
        text.ShouldContain("No balances.");
    }

    [Test]
    public void Render_WritesCashThenHoldingsAndCurrencySubtotals()
    {
        var pdf = AdviserBalancesPdf.Render(Model(
            [
                new AdviserBalancesPdfCashRow("Ann Customer", "Everyday", "Bank", "10.00 NZD"),
                new AdviserBalancesPdfHoldingRow("Ann Customer", "Everyday", "VTI", "12.5", "100.00 USD", "250.00 USD"),
                new AdviserBalancesPdfCashRow("Cee", "Broker", "Brokerage", "3.00 USD")
            ],
            [
                new AdviserBalancesPdfSubtotal("NZD", "10.00 NZD", "0.00 NZD"),
                new AdviserBalancesPdfSubtotal("USD", "3.00 USD", "250.00 USD")
            ],
            includeClosedAccounts: true));

        var text = Read(pdf);
        text.ShouldContain("Include closed accounts: yes");
        text.ShouldNotContain("No balances.");
        var cash = text.IndexOf("10.00 NZD", StringComparison.Ordinal);
        var holding = text.IndexOf("VTI", StringComparison.Ordinal);
        var otherCash = text.IndexOf("3.00 USD", StringComparison.Ordinal);
        var subtotals = text.IndexOf("Subtotals", StringComparison.Ordinal);
        cash.ShouldBeGreaterThanOrEqualTo(0);
        holding.ShouldBeGreaterThan(cash);
        otherCash.ShouldBeGreaterThan(holding);
        subtotals.ShouldBeGreaterThan(otherCash);
        text.ShouldContain("Ann Customer");
        text.ShouldContain("Everyday");
        text.ShouldContain("Bank");
        text.ShouldContain("12.5");
        text.ShouldContain("100.00 USD");
        text.ShouldContain("250.00 USD");
        text.ShouldContain("NZD cash 10.00 NZD market value 0.00 NZD");
        text.ShouldContain("USD cash 3.00 USD market value 250.00 USD");
        var nzdSubtotal = text.IndexOf("NZD cash 10.00 NZD", StringComparison.Ordinal);
        var usdSubtotal = text.IndexOf("USD cash 3.00 USD", StringComparison.Ordinal);
        nzdSubtotal.ShouldBeGreaterThan(subtotals);
        usdSubtotal.ShouldBeGreaterThan(nzdSubtotal);
    }

    private static AdviserBalancesPdfModel Model(
        IReadOnlyList<AdviserBalancesPdfRow> rows,
        IReadOnlyList<AdviserBalancesPdfSubtotal> subtotals,
        bool includeClosedAccounts = false) =>
        new(
            "Firm Book",
            "Ann",
            AsOf,
            GeneratedAt,
            includeClosedAccounts,
            rows,
            subtotals);

    private static string Read(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }
}
