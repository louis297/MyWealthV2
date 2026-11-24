using MyWealthV2.Domain.Interest;

namespace MyWealthV2.Domain.CorporateActions;

/// <summary>The New Zealand tax kind and rate of a dividend for one Customer (dividends R33).</summary>
public sealed record DividendTax(TaxKind Kind, decimal RatePercent);

/// <summary>
/// Dividends R19, R33 and R34: the New Zealand tax on a dividend. RWT follows IR284's formula
/// <c>(a × (b + c)) − c</c>, with <c>c</c> the imputation credit plus the foreign tax, never below 0; NRWT is 30 % of
/// the gross on a New Zealand company's dividend. The Customer's <c>taxResidency</c> decides; the IRD number and the
/// elected RWT rate are not used.
/// </summary>
public static class DividendWithholding
{
    /// <summary>The kind and rate for a Customer's residency and the dividend's tax source, or null when no New Zealand
    /// tax applies (an overseas Customer and a foreign company).</summary>
    public static DividendTax? For(TaxResidency residency, DividendTaxSource taxSource) =>
        residency switch
        {
            TaxResidency.Overseas when taxSource == DividendTaxSource.NewZealand =>
                new DividendTax(TaxKind.Nrwt, WithholdingRates.DividendNrwt),
            TaxResidency.Overseas => null,
            _ => new DividendTax(TaxKind.Rwt, WithholdingRates.DividendRwt)
        };

    /// <summary>R33, R34: the tax amount, from amounts already in the Account's currency, rounded away from zero at the
    /// midpoint to <paramref name="decimalPlaces"/> and never below 0.</summary>
    public static decimal Amount(DividendTax tax, decimal gross, decimal imputationCredit, decimal foreignTax, int decimalPlaces)
    {
        var amount = tax.Kind switch
        {
            TaxKind.Rwt => tax.RatePercent / 100m * (gross + imputationCredit) - imputationCredit - foreignTax,
            TaxKind.Nrwt => tax.RatePercent / 100m * gross,
            _ => throw new DomainException($"A dividend is not taxed with {tax.Kind}.")
        };

        return Math.Max(0m, DividendTerms.Round(amount, decimalPlaces));
    }

    /// <summary>R38: the basis of the tax detail. RWT is worked out on the gross plus the imputation credit, NRWT on the
    /// gross.</summary>
    public static decimal Basis(DividendTax tax, decimal gross, decimal imputationCredit) =>
        tax.Kind == TaxKind.Rwt ? gross + imputationCredit : gross;

    /// <summary>R19: the New Zealand tax of one share, unrounded, never below 0; 0 with no tax.</summary>
    public static decimal PerShare(DividendTax? tax, DividendTerms terms)
    {
        if (tax is null)
        {
            return 0m;
        }

        var foreignPerShare = terms.AmountPerShare * terms.ForeignTaxRatePercent / 100m;
        var perShare = tax.Kind switch
        {
            TaxKind.Rwt => tax.RatePercent / 100m * (terms.AmountPerShare + terms.ImputationCreditPerShare)
                           - terms.ImputationCreditPerShare - foreignPerShare,
            TaxKind.Nrwt => tax.RatePercent / 100m * terms.AmountPerShare,
            _ => throw new DomainException($"A dividend is not taxed with {tax.Kind}.")
        };

        return Math.Max(0m, perShare);
    }

    /// <summary>R19: <c>RoundDown(entitledQuantity × (netPerShare − taxPerShare) / netPerShare, 8)</c>, never below 0,
    /// so that the New Zealand tax stays in cash. With no tax the whole entitled quantity takes part.</summary>
    public static decimal ParticipatingQuantity(decimal entitledQuantity, DividendTerms terms, DividendTax? tax)
    {
        var net = terms.NetPerShare;
        var perShare = PerShare(tax, terms);
        if (perShare <= 0m)
        {
            return entitledQuantity;
        }

        if (perShare >= net)
        {
            return 0m;
        }

        return Math.Round(entitledQuantity * (net - perShare) / net, CorporateActionRatio.QuantityDecimals,
            MidpointRounding.ToZero);
    }
}
