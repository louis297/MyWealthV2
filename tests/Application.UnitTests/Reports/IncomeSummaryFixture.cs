using MyWealthV2.Application.Reports;
using MyWealthV2.Application.TaxYears;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.UnitTests.Tax;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>
/// The worked example of docs/features/income-summary.md §1.1: the tax year read of tax year 2026 with the names the
/// report prints, made at 03:12:45 UTC on 28 November 2026.
/// </summary>
internal static class IncomeSummaryFixture
{
    private const int Aaa = TaxWorkedExample.Aaa;
    private const int Kiw = IncomeWorkedExample.Kiw;
    private const int Ozb = IncomeWorkedExample.Ozb;
    private const int A1 = TaxWorkedExample.A1;
    private const int A2 = TaxWorkedExample.A2;

    public static readonly DateTimeOffset GeneratedAt = new(2026, 11, 28, 3, 12, 45, TimeSpan.Zero);

    public static CustomerIncomeSummaryNames Names => new(
        "Demo Advisory",
        "Cee Customer",
        new Dictionary<int, string> { [A1] = "Trading", [A2] = "Old broker" },
        new Dictionary<string, int> { ["NZD"] = 2, ["USD"] = 2, ["AUD"] = 2 });

    public static IReadOnlyDictionary<int, ValuedInstrument> Instruments => new Dictionary<int, ValuedInstrument>
    {
        [Aaa] = new(Aaa, Guid.Parse("00000000-0000-0000-0000-0000000000aa"), "AAA", "Alpha Inc", "USD"),
        [Kiw] = new(Kiw, Guid.Parse("00000000-0000-0000-0000-0000000000cc"), "KIW", "Kiwi Power Ltd", "NZD"),
        [Ozb] = new(Ozb, Guid.Parse("00000000-0000-0000-0000-0000000000dd"), "OZB", "Oz Bank Ltd", "AUD")
    };

    /// <summary>The tax year 2026 of the worked example.</summary>
    public static CustomerTaxYear Example => IncomeWorkedExample.Year(2026);

    public static CustomerTaxYear Year(
        IReadOnlyList<LedgerFact> facts, IReadOnlyList<Domain.Valuations.StoredRate>? rates = null, IReadOnlyDictionary<int, TaxTreatment>? treatments = null) =>
        IncomeWorkedExample.Year(2026, facts, rates, treatments);

    public static CustomerTaxLedgerRead Read(CustomerTaxYear year) => new(
        year,
        new Dictionary<int, Guid>
        {
            [A1] = Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
            [A2] = Guid.Parse("00000000-0000-0000-0000-0000000000a2")
        },
        Instruments);

    public static CustomerIncomeSummaryContent Build(CustomerTaxYear year, TaxResidency residency = TaxResidency.NewZealand) =>
        CustomerIncomeSummaryReport.Build(Read(year), Names, residency, GeneratedAt, GeneratedAt);

    public static IncomeSummarySection Section(this CustomerIncomeSummaryContent content, int number) =>
        content.Sections.Where(section => section.Title.StartsWith($"{number}. ", StringComparison.Ordinal)).ShouldHaveSingleItem();

    /// <summary>The lines of text of a section, in order, without its tables.</summary>
    public static List<string> Texts(this IncomeSummarySection section) =>
        section.Blocks.OfType<IncomeSummaryText>().Select(text => text.Text).ToList();

    public static List<IncomeSummaryTable> Tables(this IncomeSummarySection section) =>
        section.Blocks.OfType<IncomeSummaryTable>().ToList();

    /// <summary>The table at <paramref name="index"/> of a section, as an assertion.</summary>
    public static IncomeSummaryTable Table(this IncomeSummarySection section, int index)
    {
        var tables = section.Tables();
        tables.Count.ShouldBeGreaterThan(index);
        return tables[index];
    }

    /// <summary>The blocks of a section as lines: a text as it is, a heading as "# text", a table row as its cells
    /// joined by " | ".</summary>
    public static List<string> Lines(this IncomeSummarySection section) =>
        section.Blocks.SelectMany(Lines).ToList();

    /// <summary>Every line of text and every table row of the whole report.</summary>
    public static List<string> AllLines(this CustomerIncomeSummaryContent content) =>
        content.Header.Concat(content.Sections.SelectMany(section => new[] { section.Title }.Concat(section.Lines()))).ToList();

    private static IEnumerable<string> Lines(IncomeSummaryBlock block) => block switch
    {
        IncomeSummaryText { Heading: true } text => ["# " + text.Text],
        IncomeSummaryText text => [text.Text],
        IncomeSummaryTable table => table.Rows.Select(row => string.Join(" | ", row)),
        _ => []
    };
}
