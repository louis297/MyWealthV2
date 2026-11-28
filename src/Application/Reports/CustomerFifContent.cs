namespace MyWealthV2.Application.Reports;

/// <summary>
/// The content of a FIF report (docs/features/fif-report.md §4.4): its header, its numbered sections and what the
/// footer of every page says. Plain records: <see cref="CustomerFifReport.Build"/> chooses every word and figure, and
/// <see cref="CustomerFifPdf.Render"/> only lays them out.
/// </summary>
public sealed record CustomerFifContent(
    string Title,
    IReadOnlyList<string> Header,
    IReadOnlyList<CustomerFifSection> Sections,
    bool Complete,
    string CustomerName,
    int TaxYear)
{
    /// <summary>§4.4: the title in the footer of every page.</summary>
    public string FooterTitle => $"FIF Report, tax year {TaxYear}";
}

/// <summary>One numbered section: its title, such as "1. Summary", and its blocks in order.</summary>
public sealed record CustomerFifSection(string Title, IReadOnlyList<CustomerFifBlock> Blocks);

/// <summary>A block of a section: a line of text or a table.</summary>
public abstract record CustomerFifBlock;

/// <summary>A line of text. A heading starts a part of a section, such as the block of one holding.</summary>
public sealed record CustomerFifText(string Text, bool Heading = false) : CustomerFifBlock;

/// <summary>A table: its column titles and its rows, each row a cell per column.</summary>
public sealed record CustomerFifTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) : CustomerFifBlock;

/// <summary>What the report needs besides the tax year read (R9): the names it prints and the decimal places of each currency.</summary>
public sealed record CustomerFifNames(
    string TenantName,
    string CustomerName,
    IReadOnlyDictionary<int, string> AccountNames,
    IReadOnlyDictionary<string, int> DecimalPlaces);
