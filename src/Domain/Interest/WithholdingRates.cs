namespace MyWealthV2.Domain.Interest;

/// <summary>
/// Interest-run R4: the withholding rates for individuals, from Inland Revenue's pages read 2026-11-22. They change
/// only with an amendment to that spec.
/// </summary>
public static class WithholdingRates
{
    /// <summary>The RWT rates an individual may choose.</summary>
    public static IReadOnlyList<decimal> Elective { get; } = [10.5m, 17.5m, 30m, 33m, 39m];

    /// <summary>RWT when the IRD number is held and no rate was chosen.</summary>
    public const decimal Default = 33m;

    /// <summary>RWT when the IRD number is not held.</summary>
    public const decimal NonDeclaration = 45m;

    /// <summary>NRWT on interest where no double tax agreement applies.</summary>
    public const decimal NonResident = 15m;

    public static bool IsElective(decimal ratePercent) => Elective.Contains(ratePercent);

    /// <summary>R4 for the three attributes of a tax profile.</summary>
    public static Withholding For(TaxResidency residency, bool irdNumberHeld, decimal? rwtRatePercent) =>
        residency switch
        {
            TaxResidency.Overseas => new Withholding(TaxKind.Nrwt, NonResident, WithholdingBasis.NonResident),
            _ when !irdNumberHeld => new Withholding(TaxKind.Rwt, NonDeclaration, WithholdingBasis.NonDeclaration),
            _ when rwtRatePercent is { } rate => new Withholding(TaxKind.Rwt, rate, WithholdingBasis.Elected),
            _ => new Withholding(TaxKind.Rwt, Default, WithholdingBasis.Default)
        };

    /// <summary>R4 for a stored or not-provided profile.</summary>
    public static Withholding For(CustomerTaxProfile profile) =>
        For(profile.TaxResidency, profile.IrdNumberHeld, profile.RwtRatePercent);
}
