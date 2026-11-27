namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// One tax an outside payer withheld from a dividend or from interest, as the caller states it on an External account
/// (external-income R5): the kind, the rate the payer stated and the amount, in the currency of the payment. Nothing
/// is computed from it and the amount is not checked against the rate (R4).
/// </summary>
public sealed record EnteredTax(TaxKind Kind, decimal RatePercent, decimal Amount);

/// <summary>The rules of a list of entered taxes (external-income R5–R7, R12).</summary>
public static class EnteredTaxes
{
    public const int MaxCount = 3;

    /// <summary>The scale of a stated rate.</summary>
    public const int RateDecimals = 2;

    public const string TooManyMessage = "At most 3 taxes can be entered.";
    public const string KindMessage = "Kind must be Rwt, Nrwt, or ForeignWithholding.";
    public const string RateMessage = "RatePercent must be above 0 and at most 100, with at most 2 decimal places.";
    public const string AmountMessage = "Amount must be greater than 0.";
    public const string KindTwiceMessage = "Each tax kind can be entered once.";
    public const string RwtWithNrwtMessage = "RWT and NRWT cannot both be withheld from one payment.";
    public const string AboveGrossMessage = "Tax withheld cannot be more than the gross amount.";
    public const string ForeignOnlyMessage = "Foreign tax is withheld from a foreign company's dividend only.";
    public const string NrwtOnlyMessage = "NRWT is withheld from a New Zealand company's dividend only.";

    /// <summary>R5, R6: the field and message of the first rule a list breaks, or null when all hold. A row's field is
    /// <c>Taxes[i].Kind</c>, <c>Taxes[i].RatePercent</c> or <c>Taxes[i].Amount</c>; a rule of the list is <c>Taxes</c>.</summary>
    public static (string Field, string Message)? RefusalOf(IReadOnlyList<EnteredTax> taxes) => null;

    /// <summary>R12: the field and message of the first tax whose kind does not go with the dividend's tax source, or
    /// null. <c>ForeignWithholding</c> only with <c>Foreign</c>, <c>Nrwt</c> only with <c>NewZealand</c>, <c>Rwt</c> with
    /// either.</summary>
    public static (string Field, string Message)? RefusalOf(IReadOnlyList<EnteredTax> taxes, DividendTaxSource taxSource) =>
        null;

    /// <summary>R7: true when the taxes add up to more than <paramref name="gross"/>.</summary>
    public static bool AboveGross(IEnumerable<decimal> taxAmounts, decimal gross) => false;
}
