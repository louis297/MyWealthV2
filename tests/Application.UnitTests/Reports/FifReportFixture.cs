using MyWealthV2.Application.Reports;
using MyWealthV2.Application.TaxYears;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.UnitTests.Tax;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>
/// The worked example of docs/features/fif-report.md §1.1: the tax year read of tax-year-events §1.1 for tax year 2026,
/// with the names the report prints, made at 03:12:45 UTC on 28 November 2026.
/// </summary>
internal static class FifReportFixture
{
    public const int Aaa = TaxWorkedExample.Aaa;
    public const int Eee = 20;
    public const int Nnn = 30;
    public const int Sss = 40;
    public const int Bbb = 50;

    public static readonly DateTimeOffset GeneratedAt = new(2026, 11, 28, 3, 12, 45, TimeSpan.Zero);

    public static CustomerFifNames Names => new(
        "Demo Advisory",
        "Cee Customer",
        new Dictionary<int, string> { [TaxWorkedExample.A1] = "Trading", [TaxWorkedExample.A2] = "Old broker" },
        new Dictionary<string, int> { ["NZD"] = 2, ["USD"] = 2, ["JPY"] = 0 });

    public static IReadOnlyDictionary<int, ValuedInstrument> Instruments => new Dictionary<int, ValuedInstrument>
    {
        [Aaa] = new(Aaa, Guid.Parse("00000000-0000-0000-0000-0000000000aa"), "AAA", "Alpha Inc", "USD"),
        [Eee] = new(Eee, Guid.Parse("00000000-0000-0000-0000-0000000000ee"), "EEE", "Echo Fund", "AUD"),
        [Nnn] = new(Nnn, Guid.Parse("00000000-0000-0000-0000-0000000000ff"), "NNN", "November Ltd", "USD"),
        [Sss] = new(Sss, Guid.Parse("00000000-0000-0000-0000-000000000055"), "SSS", "Sierra Fund", "USD"),
        [Bbb] = new(Bbb, Guid.Parse("00000000-0000-0000-0000-0000000000bb"), "BBB", "Bravo Inc", "USD")
    };

    /// <summary>The tax year 2026 of the worked example, valued.</summary>
    public static CustomerTaxYear Example => TaxWorkedExample.ValuedYear(2026);

    public static CustomerTaxLedgerRead Read(CustomerTaxYear year) => new(
        year,
        new Dictionary<int, Guid>
        {
            [TaxWorkedExample.A1] = Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
            [TaxWorkedExample.A2] = Guid.Parse("00000000-0000-0000-0000-0000000000a2")
        },
        Instruments);

    public static CustomerFifContent Build(CustomerTaxYear year) =>
        CustomerFifReport.Build(Read(year), Names, TaxResidency.NewZealand, GeneratedAt, GeneratedAt);

    /// <summary>The holding of AAA as another Instrument, with its own treatment and FIF income.</summary>
    public static TaxHolding As(this TaxHolding holding, int instrumentId, TaxTreatment treatment, TaxHoldingIncome? income) =>
        holding with { InstrumentId = instrumentId, Treatment = treatment, Income = income };

    public static CustomerFifSection Section(this CustomerFifContent content, int number) =>
        content.Sections.Where(section => section.Title.StartsWith($"{number}. ", StringComparison.Ordinal)).ShouldHaveSingleItem();

    /// <summary>The lines of text of a section, in order, without its tables.</summary>
    public static List<string> Texts(this CustomerFifSection section) =>
        section.Blocks.OfType<CustomerFifText>().Select(text => text.Text).ToList();

    public static List<CustomerFifTable> Tables(this CustomerFifSection section) =>
        section.Blocks.OfType<CustomerFifTable>().ToList();

    /// <summary>Every line of text and every table row of the whole report, a row as its cells joined by " | ".</summary>
    public static List<string> AllLines(this CustomerFifContent content) =>
        content.Header
            .Concat(content.Sections.SelectMany(section => new[] { section.Title }.Concat(section.Blocks.SelectMany(Lines))))
            .ToList();

    private static IEnumerable<string> Lines(CustomerFifBlock block) => block switch
    {
        CustomerFifText text => [text.Text],
        CustomerFifTable table => table.Rows.Select(row => string.Join(" | ", row)),
        _ => []
    };
}
