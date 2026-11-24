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
    public const decimal MaxAmountPerShare = 1_000_000m;

    /// <summary>The scale of an amount per share, a credit per share and a plan price.</summary>
    public const int PerShareDecimals = 8;

    /// <summary>The scale of the foreign tax rate.</summary>
    public const int RateDecimals = 2;

    /// <summary>R3: the field and message of the first term that breaks the rule, or null when all hold. The credit and
    /// the foreign tax rate default to 0 when not given.</summary>
    public static (string Field, string Message)? RefusalOf(
        decimal? amountPerShare,
        DividendTaxSource? taxSource,
        decimal? imputationCreditPerShare,
        decimal? foreignTaxRatePercent,
        decimal? reinvestmentPrice)
    {
        if (amountPerShare is not { } amount || amount <= 0m || amount > MaxAmountPerShare
            || !HasScale(amount, PerShareDecimals))
        {
            return ("AmountPerShare",
                "The amount per share is above 0, at most 1 000 000, with at most 8 decimal places.");
        }

        if (taxSource is not { } source || !Enum.IsDefined(source))
        {
            return ("TaxSource", "The tax source is NewZealand or Foreign.");
        }

        var credit = imputationCreditPerShare ?? 0m;
        if (credit < 0m || !HasScale(credit, PerShareDecimals) || credit > amount)
        {
            return ("ImputationCreditPerShare",
                "The imputation credit per share is 0 or more, with at most 8 decimal places, and not above the amount per share.");
        }

        if (credit > 0m && source != DividendTaxSource.NewZealand)
        {
            return ("ImputationCreditPerShare", "Only a New Zealand company attaches imputation credits.");
        }

        var rate = foreignTaxRatePercent ?? 0m;
        if (rate is < 0m or >= 100m || !HasScale(rate, RateDecimals))
        {
            return ("ForeignTaxRatePercent",
                "The foreign tax rate is 0 or more and below 100, with at most 2 decimal places.");
        }

        if (rate > 0m && source != DividendTaxSource.Foreign)
        {
            return ("ForeignTaxRatePercent", "Only a foreign company's dividend has foreign tax.");
        }

        if (reinvestmentPrice is { } price && (price <= 0m || !HasScale(price, PerShareDecimals)))
        {
            return ("ReinvestmentPrice", "The reinvestment price is above 0, with at most 8 decimal places.");
        }

        return null;
    }

    /// <summary>R3: the terms, or a <see cref="DomainException"/> when one breaks the rule.</summary>
    public static DividendTerms Create(
        decimal amountPerShare,
        DividendTaxSource taxSource,
        decimal? imputationCreditPerShare,
        decimal? foreignTaxRatePercent,
        decimal? reinvestmentPrice)
    {
        if (RefusalOf(amountPerShare, taxSource, imputationCreditPerShare, foreignTaxRatePercent, reinvestmentPrice)
            is { } refusal)
        {
            throw new DomainException(refusal.Message);
        }

        return new DividendTerms(
            amountPerShare, taxSource, imputationCreditPerShare ?? 0m, foreignTaxRatePercent ?? 0m, reinvestmentPrice);
    }

    /// <summary>The dividend per share net of foreign tax, unrounded (R19, R22).</summary>
    public decimal NetPerShare => AmountPerShare * (1m - ForeignTaxRatePercent / 100m);

    /// <summary>R14: the source amounts of an entitled quantity, each rounded away from zero at the midpoint to
    /// <paramref name="decimalPlaces"/>; the foreign tax is taken from the rounded gross.</summary>
    public DividendSourceAmounts SourceAmounts(decimal quantity, int decimalPlaces)
    {
        var gross = Round(quantity * AmountPerShare, decimalPlaces);
        var credit = Round(quantity * ImputationCreditPerShare, decimalPlaces);
        var foreign = Round(gross * ForeignTaxRatePercent / 100m, decimalPlaces);
        return new DividendSourceAmounts(gross, credit, foreign);
    }

    internal static decimal Round(decimal value, int decimalPlaces) =>
        decimal.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);

    private static bool HasScale(decimal value, int decimals) => decimal.Round(value, decimals) == value;
}
