namespace MyWealthV2.Domain.CorporateActions;

/// <summary>The gross, imputation credit and foreign tax of one entitlement, in the quote currency (dividends R14).</summary>
public sealed record DividendSourceAmounts(decimal Gross, decimal ImputationCredit, decimal ForeignTax);

/// <summary>
/// The terms of a dividend, per share and in the Instrument's quote currency (dividends R3). They never hold an amount
/// for an account.
/// </summary>
public sealed record DividendTerms(
    decimal AmountPerShare,
    DividendTaxSource TaxSource,
    decimal ImputationCreditPerShare,
    decimal ForeignTaxRatePercent,
    decimal? ReinvestmentPrice)
{
    public static (string Field, string Message)? RefusalOf(
        decimal? amountPerShare,
        DividendTaxSource? taxSource,
        decimal? imputationCreditPerShare,
        decimal? foreignTaxRatePercent,
        decimal? reinvestmentPrice) =>
        throw new NotImplementedException();

    public static DividendTerms Create(
        decimal amountPerShare,
        DividendTaxSource taxSource,
        decimal? imputationCreditPerShare,
        decimal? foreignTaxRatePercent,
        decimal? reinvestmentPrice) =>
        throw new NotImplementedException();

    public decimal NetPerShare => throw new NotImplementedException();

    public DividendSourceAmounts SourceAmounts(decimal quantity, int decimalPlaces) =>
        throw new NotImplementedException();
}
