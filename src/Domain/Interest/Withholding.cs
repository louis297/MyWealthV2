namespace MyWealthV2.Domain.Interest;

/// <summary>The tax kind, rate and basis a tax profile gives (interest-run R4).</summary>
public sealed record Withholding(TaxKind Kind, decimal RatePercent, WithholdingBasis Basis);
