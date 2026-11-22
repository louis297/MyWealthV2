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

    [Test]
    public void Render_PlacesReversalImmediatelyAfterItsOriginal()
    {
        var pdf = CustomerTransactionsPdf.Render(new CustomerTransactionsPdfModel(
            "Firm Queue",
            new DateTimeOffset(2026, 3, 31, 14, 5, 6, TimeSpan.Zero),
            "Cee",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31),
            IncludeClosedAccounts: false,
            [
                new CustomerTransactionsPdfRow(
                    new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero),
                    "Everyday",
                    "TransferIn",
                    "10.00 NZD",
                    "",
                    "",
                    "salary"),
                new CustomerTransactionsPdfRow(
                    new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero),
                    "Everyday",
                    "Reversal",
                    "-10.00 NZD",
                    "",
                    "",
                    "undo salary")
            ]));

        var text = Read(pdf);
        text.ShouldNotContain("No transactions in this range.");
        var original = text.IndexOf("salary", StringComparison.Ordinal);
        var reversal = text.IndexOf("Reversal", StringComparison.Ordinal);
        var undo = text.IndexOf("undo salary", StringComparison.Ordinal);
        original.ShouldBeGreaterThanOrEqualTo(0);
        reversal.ShouldBeGreaterThan(original);
        undo.ShouldBeGreaterThan(reversal);
    }

    [Test]
    public void Render_ShowsTheFeeColumn()
    {
        var pdf = CustomerTransactionsPdf.Render(new CustomerTransactionsPdfModel(
            "Firm Queue",
            new DateTimeOffset(2026, 3, 31, 14, 5, 6, TimeSpan.Zero),
            "Cee",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31),
            IncludeClosedAccounts: false,
            [
                new CustomerTransactionsPdfRow(
                    new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero),
                    "Broker",
                    "Buy",
                    "-1260.00 USD",
                    "VTI",
                    "50",
                    "first buy",
                    Fee: "-10.00 USD")
            ]));

        var text = Read(pdf);
        var cash = text.IndexOf("Cash", StringComparison.Ordinal);
        var fee = text.IndexOf("Fee", StringComparison.Ordinal);
        var instrument = text.IndexOf("Instrument", StringComparison.Ordinal);
        fee.ShouldBeGreaterThan(cash);
        instrument.ShouldBeGreaterThan(fee);
        text.IndexOf("-10.00 USD", StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("-1260.00 USD", StringComparison.Ordinal));
    }

    [Test]
    public void Render_ShowsTheTaxColumn_AfterTheFeeColumn()
    {
        var pdf = CustomerTransactionsPdf.Render(new CustomerTransactionsPdfModel(
            "Firm Queue",
            new DateTimeOffset(2027, 1, 31, 14, 5, 6, TimeSpan.Zero),
            "Cee",
            new DateOnly(2027, 1, 1),
            new DateOnly(2027, 1, 31),
            IncludeClosedAccounts: false,
            [
                new CustomerTransactionsPdfRow(
                    new DateTimeOffset(2027, 1, 1, 0, 30, 4, TimeSpan.Zero),
                    "Managed",
                    "Interest",
                    "10.17 USD",
                    "",
                    "",
                    "Interest",
                    Tax: "-2.16 USD")
            ]));

        var text = Read(pdf);
        var fee = text.IndexOf("Fee", StringComparison.Ordinal);
        var tax = text.IndexOf("Tax", StringComparison.Ordinal);
        var instrument = text.IndexOf("Instrument", StringComparison.Ordinal);
        tax.ShouldBeGreaterThan(fee);
        instrument.ShouldBeGreaterThan(tax);
        text.IndexOf("-2.16 USD", StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("10.17 USD", StringComparison.Ordinal));
    }

    private const string Note =
        "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.";

    [Test]
    public void Render_ShowsTheCustodyColumnAfterTheAccount_AndTheNoteWhenARowIsHeldElsewhere()
    {
        var pdf = CustomerTransactionsPdf.Render(Model(
            [
                Row("Managed", "1.00 USD", "Held on platform", "managed in"),
                Row("Everyday", "2.00 NZD", "Held elsewhere", "everyday in")
            ],
            showExternalNote: true));

        var text = Read(pdf);
        var account = text.IndexOf("Account", text.IndexOf("Booked at", StringComparison.Ordinal), StringComparison.Ordinal);
        var custody = text.IndexOf("Custody", StringComparison.Ordinal);
        custody.ShouldBeGreaterThan(account);
        text.IndexOf("Type", custody, StringComparison.Ordinal).ShouldBeGreaterThan(custody);

        var managed = text.IndexOf("Managed", StringComparison.Ordinal);
        var onPlatform = text.IndexOf("Held on platform", StringComparison.Ordinal);
        var managedCash = text.IndexOf("1.00 USD", StringComparison.Ordinal);
        onPlatform.ShouldBeGreaterThan(managed);
        managedCash.ShouldBeGreaterThan(onPlatform);
        var elsewhere = text.IndexOf("Held elsewhere", managedCash, StringComparison.Ordinal);
        elsewhere.ShouldBeGreaterThan(text.IndexOf("Everyday", StringComparison.Ordinal));

        text.ShouldContain(Note);
        text.IndexOf(Note, StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("everyday in", StringComparison.Ordinal));
    }

    [Test]
    public void Render_HasNoNoteWithoutARowHeldElsewhere()
    {
        var withoutNote = Read(CustomerTransactionsPdf.Render(Model([Row("Managed", "1.00 USD", "Held on platform", "in")])));
        withoutNote.ShouldContain("Held on platform");
        withoutNote.ShouldNotContain("The platform has not checked these figures.");

        var empty = Read(CustomerTransactionsPdf.Render(Model([])));
        empty.ShouldContain("No transactions in this range.");
        empty.ShouldNotContain("The platform has not checked these figures.");
    }

    private static CustomerTransactionsPdfModel Model(
        IReadOnlyList<CustomerTransactionsPdfRow> rows,
        bool showExternalNote = false) =>
        new(
            "Firm Queue",
            new DateTimeOffset(2026, 3, 31, 14, 5, 6, TimeSpan.Zero),
            "Cee",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31),
            IncludeClosedAccounts: false,
            rows,
            showExternalNote);

    private static CustomerTransactionsPdfRow Row(string account, string cash, string custody, string memo) =>
        new(
            new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero),
            account,
            "TransferIn",
            cash,
            "",
            "",
            memo,
            Custody: custody);

    internal static string Read(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }
}
