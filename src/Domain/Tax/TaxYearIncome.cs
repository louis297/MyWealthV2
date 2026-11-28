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

        // R2, R6, R7: the dividends of each tax holding, in the group of its treatment.
        var rows = holdings
            .Select(holding => (Holding: holding, Group: GroupOf(holding.Treatment.FifTreatment), Dividends: holding.Events
                .Select(e => e.Dividend)
                .OfType<TaxDividendAmounts>()
                .ToList()))
            .Where(row => row.Dividends.Count > 0)
            .ToList();
        var groups = Enum.GetValues<IncomeGroup>()
            .Select(group =>
            {
                var inGroup = rows.Where(row => row.Group == group).ToList();
                var holdingRows = inGroup
                    .Select(row => new IncomeHoldingRow(row.Holding.InstrumentId, row.Holding.Treatment.IssuerCountry, DividendSum(row.Dividends, zero)))
                    .ToList();
                var countries = group is IncomeGroup.ForeignExempt or IncomeGroup.Attributing
                    ? inGroup
                        .GroupBy(row => CountryOf(row.Holding.Treatment.IssuerCountry))
                        .OrderBy(country => country.Key is null)
                        .ThenBy(country => country.Key, StringComparer.Ordinal)
                        .Select(country => new IncomeCountryRow(country.Key, DividendSum(country.SelectMany(row => row.Dividends), zero)))
                        .ToList()
                    : [];
                return new IncomeDividendGroup(group, holdingRows, countries, DividendSum(inGroup.SelectMany(row => row.Dividends), zero));
            })
            .ToList();
        var allDividends = rows.SelectMany(row => row.Dividends).ToList();
        var dividends = new IncomeDividends(groups, DividendSum(allDividends, zero));

        // R8: the interest items per Account, in the order of the first item of each.
        var accounts = interest
            .GroupBy(item => item.Item.AccountId)
            .Select(account => new IncomeAccountRow(
                account.Key, account.First().Item.CustodyMode, account.First().Item.Currency, InterestSum(account.ToList(), zero)))
            .ToList();
        var interestSums = new IncomeInterest(accounts, InterestSum(interest, zero));

        // R9, R10: the tax taken over every payment; null when any payment has a missing figure.
        var anyMissing = allDividends.Any(Missing) || interest.Any(Missing);
        var taxWithheld = anyMissing
            ? new IncomeTaxWithheld(null, null, null)
            : new IncomeTaxWithheld(
                zero + allDividends.Sum(d => d.Rwt!.Value) + interest.Sum(i => i.Rwt!.Value),
                zero + allDividends.Sum(d => d.Nrwt!.Value) + interest.Sum(i => i.Nrwt!.Value),
                zero + allDividends.Sum(d => d.ForeignTax!.Value) + interest.Sum(i => i.ForeignTax!.Value));
        var imputationCredit = allDividends.Any(Missing) ? (decimal?)null : zero + allDividends.Sum(d => d.ImputationCredit!.Value);

        // R11: the reasons, each once: of the tax holdings in their order, then of the Accounts in theirs.
        var incomplete = new List<IncomeReason>();
        foreach (var row in rows)
        {
            if (row.Dividends.Any(Missing))
            {
                incomplete.Add(new IncomeReason(IncomeIncompleteReason.AmountMissing, row.Holding.InstrumentId, null));
            }

            if (row.Group == IncomeGroup.NotClassified)
            {
                incomplete.Add(new IncomeReason(IncomeIncompleteReason.NotClassified, row.Holding.InstrumentId, null));
            }
        }

        incomplete.AddRange(accounts
            .Where(account => interest.Any(item => item.Item.AccountId == account.AccountId && Missing(item)))
            .Select(account => new IncomeReason(IncomeIncompleteReason.AmountMissing, null, account.AccountId)));

        return new TaxYearIncome(
            dividends,
            interestSums,
            taxWithheld,
            imputationCredit,
            incomplete.Count == 0,
            incomplete,
            NoticesOf(holdings, rows.Select(row => row.Holding).ToList(), interest));
    }

    /// <summary>R6: the group of a treatment.</summary>
    public static IncomeGroup GroupOf(FifTreatment treatment) => treatment switch
    {
        FifTreatment.NotForeign => IncomeGroup.NewZealand,
        _ when treatment.IsExemption() => IncomeGroup.ForeignExempt,
        _ when treatment.IsAttributing() => IncomeGroup.Attributing,
        _ => IncomeGroup.NotClassified
    };

    /// <summary>R12: the notices, each once, in the order of <see cref="IncomeNotice"/>; those of an Account by its id.</summary>
    private static List<IncomeNoticeLine> NoticesOf(
        IReadOnlyList<TaxHolding> holdings, IReadOnlyList<TaxHolding> withDividend, IReadOnlyList<TaxYearInterest> interest)
    {
        var notices = new List<IncomeNoticeLine>();
        var external = withDividend
            .SelectMany(holding => holding.Events.Where(e => e.Dividend is not null).Select(e => e.Event))
            .Where(e => e.CustodyMode == CustodyMode.External)
            .Select(e => e.AccountId)
            .Concat(interest.Where(item => item.Item.CustodyMode == CustodyMode.External).Select(item => item.Item.AccountId))
            .Distinct()
            .Order();
        notices.AddRange(external.Select(accountId => new IncomeNoticeLine(IncomeNotice.ExternalFigures, null, accountId)));
        notices.AddRange(withDividend
            .Where(holding => holding.Notices.Contains(TaxNotice.DividendTaxSourceDisagrees))
            .Select(holding => new IncomeNoticeLine(IncomeNotice.DividendTaxSourceDisagrees, holding.InstrumentId, null)));
        notices.AddRange(holdings
            .Where(holding => holding.Notices.Contains(TaxNotice.Scrip))
            .Select(holding => new IncomeNoticeLine(IncomeNotice.Scrip, holding.InstrumentId, null)));
        notices.AddRange(interest
            .Where(item => item.Item.Gross < 0m)
            .Select(item => item.Item.AccountId)
            .Distinct()
            .Order()
            .Select(accountId => new IncomeNoticeLine(IncomeNotice.InterestCharged, null, accountId)));
        return notices;
    }

    /// <summary>R7, R10: the sums of the dividends, every figure <c>null</c> when one of them has a missing figure.</summary>
    private static IncomeDividendSum DividendSum(IEnumerable<TaxDividendAmounts> dividends, decimal zero)
    {
        var list = dividends.ToList();
        if (list.Any(Missing))
        {
            return new IncomeDividendSum(list.Count, null, null, null, null, null, null);
        }

        var gross = zero + list.Sum(d => d.Gross!.Value);
        var credit = zero + list.Sum(d => d.ImputationCredit!.Value);
        return new IncomeDividendSum(
            list.Count,
            gross,
            credit,
            gross + credit,
            zero + list.Sum(d => d.ForeignTax!.Value),
            zero + list.Sum(d => d.Rwt!.Value),
            zero + list.Sum(d => d.Nrwt!.Value));
    }

    /// <summary>R8, R10: received and charged apart, by the sign of the gross as booked; every figure <c>null</c> when an
    /// item has a missing figure.</summary>
    private static IncomeInterestSum InterestSum(IReadOnlyList<TaxYearInterest> items, decimal zero)
    {
        if (items.Any(Missing))
        {
            return new IncomeInterestSum(items.Count, null, null, null, null, null);
        }

        return new IncomeInterestSum(
            items.Count,
            zero + items.Where(item => item.Item.Gross > 0m).Sum(item => item.Amount!.Value),
            zero - items.Where(item => item.Item.Gross < 0m).Sum(item => item.Amount!.Value),
            zero + items.Sum(item => item.Rwt!.Value),
            zero + items.Sum(item => item.Nrwt!.Value),
            zero + items.Sum(item => item.ForeignTax!.Value));
    }

    private static bool Missing(TaxDividendAmounts dividend) =>
        dividend.Gross is null || dividend.ForeignTax is null || dividend.Rwt is null || dividend.Nrwt is null || dividend.ImputationCredit is null;

    private static bool Missing(TaxYearInterest item) =>
        item.Amount is null || item.Rwt is null || item.Nrwt is null || item.ForeignTax is null;

    private static string? CountryOf(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
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
