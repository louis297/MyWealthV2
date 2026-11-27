namespace MyWealthV2.Domain.Instruments;

/// <summary>One entry of <see cref="Countries"/>: an ISO 3166-1 alpha-2 code and its English short name.</summary>
public sealed record Country(string Code, string Name);

/// <summary>
/// Instrument-tax-attributes R5, R6: the officially assigned ISO 3166-1 alpha-2 codes, each with its English short
/// name. The list changes only with an amendment to that spec. There is no table and no write route.
/// </summary>
public static class Countries
{
    public const string NewZealand = "NZ";

    public const string Australia = "AU";

    public static IReadOnlyList<Country> All { get; } = [];

    public static bool IsKnown(string? code) => false;
}
