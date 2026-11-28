namespace MyWealthV2.Domain.Tax;

/// <summary>
/// The income summary of one Customer for one tax year (docs/features/income-summary.md R6–R14): the dividends of the
/// tax holdings in four groups, the interest items per Account, the tax taken and the imputation credits, in NZD. It
/// adds up and decides nothing. A figure is <c>null</c> when a payment it counts has a missing figure (R10).
/// </summary>
public sealed record TaxYearIncome(
    IncomeDividends Dividends,
    IncomeInterest Interest,
    IncomeTaxWithheld TaxWithheld,
    decimal? ImputationCredit,
    bool Complete,
    IReadOnlyList<IncomeReason> Incomplete,
    IReadOnlyList<IncomeNoticeLine> Notices)
{
    /// <summary>
    /// R6–R12 over the tax holdings of the year and its interest items. No I/O and no clock;
    /// <paramref name="decimalPlaces"/> are NZD's, for a sum of nothing.
    /// </summary>
    public static TaxYearIncome Compute(IReadOnlyList<TaxHolding> holdings, IReadOnlyList<TaxYearInterest> interest, int decimalPlaces)
    {
        var zero = TaxFx.Zero(decimalPlaces);
        var noDividends = new IncomeDividendSum(0, zero, zero, zero, zero, zero, zero);
        var noInterest = new IncomeInterestSum(0, zero, zero, zero, zero, zero);
        return new TaxYearIncome(
            new IncomeDividends(Enum.GetValues<IncomeGroup>().Select(group => new IncomeDividendGroup(group, [], [], noDividends)).ToList(), noDividends),
            new IncomeInterest([], noInterest),
            new IncomeTaxWithheld(zero, zero, zero),
            zero,
            false,
            [],
            []);
    }
}

/// <summary>R13: the four groups of R6, in order, and the sum over all dividends.</summary>
public sealed record IncomeDividends(IReadOnlyList<IncomeDividendGroup> Groups, IncomeDividendSum Total);

/// <summary>R7, R13: a group with a row per tax holding that has a dividend, a row per issuer country in the two foreign
/// groups (empty in the others), and the group's sum.</summary>
public sealed record IncomeDividendGroup(
    IncomeGroup Group,
    IReadOnlyList<IncomeHoldingRow> Holdings,
    IReadOnlyList<IncomeCountryRow> Countries,
    IncomeDividendSum Total);

/// <summary>A tax holding with a dividend in the year, with the sum of its dividends.</summary>
public sealed record IncomeHoldingRow(int InstrumentId, string? IssuerCountry, IncomeDividendSum Sum);

/// <summary>An issuer country of a foreign group, <c>null</c> for a holding with none, with the sum of its dividends.</summary>
public sealed record IncomeCountryRow(string? IssuerCountry, IncomeDividendSum Sum);

/// <summary>R7: the number of dividends and the sums of their NZD figures. <see cref="GrossWithImputation"/> is
/// <c>gross + imputationCredit</c>, the gross dividend of Inland Revenue's return.</summary>
public sealed record IncomeDividendSum(
    int Count,
    decimal? Gross,
    decimal? ImputationCredit,
    decimal? GrossWithImputation,
    decimal? ForeignTax,
    decimal? Rwt,
    decimal? Nrwt);

/// <summary>R8, R13: a row per Account with an interest item, in the order of its first item, and the total.</summary>
public sealed record IncomeInterest(IReadOnlyList<IncomeAccountRow> Accounts, IncomeInterestSum Total);

/// <summary>An Account with an interest item in the year, with the sum of its items.</summary>
public sealed record IncomeAccountRow(int AccountId, CustodyMode CustodyMode, string Currency, IncomeInterestSum Sum);

/// <summary>R8: interest received (items above 0) and interest charged (items below 0, as a positive figure) apart,
/// never netted; <see cref="Count"/> counts both.</summary>
public sealed record IncomeInterestSum(
    int Count,
    decimal? Received,
    decimal? Charged,
    decimal? Rwt,
    decimal? Nrwt,
    decimal? ForeignTax);

/// <summary>R9: the tax taken over all dividends and all interest items, as booked.</summary>
public sealed record IncomeTaxWithheld(decimal? Rwt, decimal? Nrwt, decimal? ForeignTax);

/// <summary>R11: a reason the summary is incomplete, of a tax holding or of an Account.</summary>
public sealed record IncomeReason(IncomeIncompleteReason Reason, int? InstrumentId, int? AccountId);

/// <summary>R12: a notice, of a tax holding or of an Account.</summary>
public sealed record IncomeNoticeLine(IncomeNotice Notice, int? InstrumentId, int? AccountId);
