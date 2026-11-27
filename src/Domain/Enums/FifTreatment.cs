namespace MyWealthV2.Domain.Enums;

/// <summary>
/// Instrument-tax-attributes R7, R13: how the FIF rules treat a holding of an Instrument in a tax year, as the person
/// who classifies states it. <see cref="NotClassified"/> is the answer when no row stands for a tax year; it is never
/// stored.
/// </summary>
public enum FifTreatment
{
    /// <summary>Nobody has said. A result that depends on it is incomplete.</summary>
    NotClassified = 0,

    /// <summary>The issuer is resident in New Zealand, so a holding is not a FIF interest.</summary>
    NotForeign = 1,

    /// <summary>For this tax year a share in an ASX-listed Australian resident company (section EX 31).</summary>
    ExemptAsxListed = 2,

    /// <summary>For this tax year not an attributing interest for another reason, which the note names.</summary>
    ExemptOther = 3,

    /// <summary>An attributing interest for which the fair dividend rate method is allowed.</summary>
    Attributing = 4,

    /// <summary>An attributing interest of a kind the fair dividend rate method cannot be used for.</summary>
    AttributingNoFdr = 5
}

public static class FifTreatmentExtensions
{
    /// <summary>R7: a value a row may hold. <see cref="FifTreatment.NotClassified"/> is never stored.</summary>
    public static bool IsStored(this FifTreatment treatment) =>
        treatment is >= FifTreatment.NotForeign and <= FifTreatment.AttributingNoFdr;

    /// <summary>R10: an exemption stands for its own tax year only.</summary>
    public static bool IsExemption(this FifTreatment treatment) =>
        treatment is FifTreatment.ExemptAsxListed or FifTreatment.ExemptOther;

    /// <summary>An attributing interest, with or without the fair dividend rate method.</summary>
    public static bool IsAttributing(this FifTreatment treatment) =>
        treatment is FifTreatment.Attributing or FifTreatment.AttributingNoFdr;
}
