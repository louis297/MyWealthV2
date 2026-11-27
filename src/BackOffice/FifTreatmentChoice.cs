namespace MyWealthV2.BackOffice;

/// <summary>
/// The FIF treatment select and texts of the instrument pages (instrument-tax-attributes BO-I9, BO-I10, BO-I12).
/// </summary>
public static class FifTreatmentChoice
{
    public const string NotClassified = "Not classified";

    public const string Explanation =
        "A treatment that is not an exemption stands for the later tax years until the next line. An exemption stands for its own tax year only.";

    /// <summary>The five values a line may hold, in the order shown.</summary>
    public static IReadOnlyList<string> Values { get; } = [];

    /// <summary>BO-I10: the label of a value.</summary>
    public static string Label(string? value) => value ?? "";

    /// <summary>BO-I12: the FIF treatment column of the list, from the row with the latest tax year.</summary>
    public static string ListText(IReadOnlyList<FifTreatmentItem>? rows) => "";

    /// <summary>BO-I9: a country of the picker, by name and code.</summary>
    public static string CountryLabel(string? code, IReadOnlyList<CountryItem> countries) => code ?? "";
}
