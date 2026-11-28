namespace MyWealthV2.Application.Reports;

/// <summary>
/// The content of a Tax Year Income Summary (docs/features/income-summary.md §4.4): its header, its numbered sections
/// and what the footer of every page says. Plain records: <see cref="CustomerIncomeSummaryReport.Build"/> chooses every
/// word and figure, and <see cref="CustomerIncomeSummaryPdf.Render"/> only lays them out.
/// </summary>
public sealed record CustomerIncomeSummaryContent(
    string Title,
    IReadOnlyList<string> Header,
    IReadOnlyList<IncomeSummarySection> Sections,
    bool Complete,
    string CustomerName,
    int TaxYear)
{
    /// <summary>§4.4: the title in the footer of every page.</summary>
    public string FooterTitle => $"Tax Year Income Summary, tax year {TaxYear}";
}

/// <summary>One numbered section: its title, such as "1. Summary", and its blocks in order.</summary>
public sealed record IncomeSummarySection(string Title, IReadOnlyList<IncomeSummaryBlock> Blocks);

/// <summary>A block of a section: a line of text or a table.</summary>
public abstract record IncomeSummaryBlock;

/// <summary>A line of text. A heading starts a part of a section, such as the block of one group.</summary>
public sealed record IncomeSummaryText(string Text, bool Heading = false) : IncomeSummaryBlock;

/// <summary>A table: its column titles and its rows, each row a cell per column.</summary>
public sealed record IncomeSummaryTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) : IncomeSummaryBlock;

/// <summary>What the report needs besides the tax year read (R20): the names it prints and the decimal places of each
/// currency.</summary>
public sealed record CustomerIncomeSummaryNames(
    string TenantName,
    string CustomerName,
    IReadOnlyDictionary<int, string> AccountNames,
    IReadOnlyDictionary<string, int> DecimalPlaces);
