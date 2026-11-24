namespace MyWealthV2.Domain.CorporateActions;

/// <summary>The New Zealand tax kind and rate of a dividend for one Customer (dividends R33).</summary>
public sealed record DividendTax(TaxKind Kind, decimal RatePercent);

/// <summary>Dividends R19, R33 and R34: the New Zealand tax on a dividend.</summary>
public static class DividendWithholding
{
    public static DividendTax? For(TaxResidency residency, DividendTaxSource taxSource) =>
        throw new NotImplementedException();

    public static decimal Amount(DividendTax tax, decimal gross, decimal imputationCredit, decimal foreignTax, int decimalPlaces) =>
        throw new NotImplementedException();

    public static decimal Basis(DividendTax tax, decimal gross, decimal imputationCredit) =>
        throw new NotImplementedException();

    public static decimal PerShare(DividendTax? tax, DividendTerms terms) =>
        throw new NotImplementedException();

    public static decimal ParticipatingQuantity(decimal entitledQuantity, DividendTerms terms, DividendTax? tax) =>
        throw new NotImplementedException();
}
