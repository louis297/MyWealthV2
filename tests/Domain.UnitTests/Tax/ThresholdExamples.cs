using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>
/// Examples 2 and 3 of docs/features/tax-cost-threshold.md §1.1. A1 is an Account in USD and A2 one in NZD, as in
/// <see cref="TaxWorkedExample"/>; the Transactions have the internal ids of their order in the table.
/// </summary>
internal static class ThresholdExamples
{
    public const int Bbb = 20;
    public const int Ccc = 30;
    public const int Ddd = 40;
    public const int Eee = 50;

    public static TaxTreatment Attributing => new(FifTreatment.Attributing, 2009, null, "US");

    public static TaxTreatment Asx => new(FifTreatment.ExemptAsxListed, 2009, null, "AU");

    /// <summary>Example 2, Inland Revenue's Jax: <c>AAA</c> and <c>BBB</c> bought on 31 March 2022 in Auckland, half of
    /// each sold in January 2024 and the money put into exempt <c>CCC</c>.</summary>
    public static IReadOnlyList<LedgerFact> Jax =>
    [
        Fact(1, Utc(2022, 3, 30, 22, 0), TransactionType.Buy,
            [Cash(A2, CashLegRole.Principal, -29_985.00m), Cash(A2, CashLegRole.Fee, -15.00m)], [Security(1, A2, 200m, 0m)]),
        Fact(2, Utc(2022, 3, 30, 22, 0), TransactionType.Buy,
            [Cash(A2, CashLegRole.Principal, -24_990.00m), Cash(A2, CashLegRole.Fee, -10.00m)], [Security(2, A2, 500m, 0m, Bbb)]),
        Fact(3, Utc(2024, 1, 15, 21, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 32_150.00m)], [Security(3, A2, -100m, 0m)]),
        Fact(4, Utc(2024, 1, 15, 21, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 0.01m)], [Security(4, A2, -250m, 0m, Bbb)]),
        Fact(5, Utc(2024, 1, 16, 21, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -32_150.00m)], [Security(5, A2, 4_000m, 0m, Ccc)])
    ];

    public static IReadOnlyDictionary<int, TaxTreatment> JaxTreatments => new Dictionary<int, TaxTreatment>
    {
        [Aaa] = Attributing,
        [Bbb] = Attributing,
        [Ccc] = Asx
    };

    /// <summary>Example 3: a purchase in USD crosses the threshold. U1 to U5 have the ids 1 to 5.</summary>
    public static IReadOnlyList<LedgerFact> Crossing =>
    [
        Fact(1, Utc(2025, 4, 2, 1, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -30_000.00m)], [Security(1, A2, 1_000m, 0m, Eee)]),
        Fact(2, Utc(2025, 5, 12, 20, 0), TransactionType.Buy,
            [Cash(A1, CashLegRole.Principal, -25_000.00m), Cash(A1, CashLegRole.Fee, -10.00m)], [Security(2, A1, 500m, 25_010.00m, Bbb)]),
        Fact(3, Utc(2025, 8, 4, 3, 0), TransactionType.StockIn, [], [Security(3, A1, 60m, 2_400.00m, Ddd)],
            inKind: new LedgerInKindTransfer(false, new DateOnly(2024, 6, 14))),
        Fact(4, Utc(2025, 10, 9, 19, 30), TransactionType.Buy,
            [Cash(A1, CashLegRole.Principal, -2_200.00m), Cash(A1, CashLegRole.Fee, -10.00m)], [Security(4, A1, 40m, 2_210.00m, Bbb)]),
        Fact(5, Utc(2025, 11, 20, 20, 10), TransactionType.Sell,
            [Cash(A1, CashLegRole.Principal, 14_850.00m), Cash(A1, CashLegRole.Fee, -10.00m)], [Security(5, A1, -270m, -13_605.00m, Bbb)])
    ];

    public static IReadOnlyDictionary<int, TaxTreatment> CrossingTreatments => new Dictionary<int, TaxTreatment>
    {
        [Bbb] = Attributing,
        [Ddd] = Attributing,
        [Eee] = Asx
    };

    /// <summary>The stored rates USD → NZD of example 3; <paramref name="october9"/> is the rate of 9 October 2025.</summary>
    public static IReadOnlyList<StoredRate> CrossingRates(decimal october9 = 1.7500m) =>
        [Rate(2024, 6, 14, 1.6300m), Rate(2025, 5, 12, 1.6900m), Rate(2025, 10, 9, october9)];

    /// <summary>What Application reads for the walk: for each key of <see cref="TaxCostLedger.RateKeys"/>, the row with the
    /// latest date on or before it.</summary>
    public static IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> RatesOfWalk(
        IReadOnlyList<TaxEvent> events, IReadOnlySet<int> costed, IReadOnlyList<StoredRate> rows) =>
        TaxCostLedger.RateKeys(events, costed)
            .Select(key => (key, row: rows.Where(row => row.AsOfDate <= key.Date).MaxBy(row => row.AsOfDate)))
            .Where(pair => pair.row is not null)
            .ToDictionary(pair => pair.key, pair => pair.row!);

    /// <summary>The test of <paramref name="taxYear"/>: the walk of the Instruments whose treatment needs a value
    /// (R4), with the threshold of the year unless one is given.</summary>
    public static ThresholdTest Test(
        int taxYear,
        IReadOnlyList<LedgerFact> facts,
        IReadOnlyDictionary<int, TaxTreatment> treatments,
        IReadOnlyList<StoredRate>? rates = null,
        IReadOnlyDictionary<(int InstrumentId, int TaxYear), FifTreatment>? earlierTreatments = null,
        decimal? threshold = null,
        bool noThreshold = false)
    {
        var events = TaxLedger.Build(facts);
        var costed = events.Select(e => e.InstrumentId)
            .Where(id => CustomerTaxYear.NeedsValue((treatments.GetValueOrDefault(id) ?? TaxTreatment.NotClassified()).FifTreatment))
            .ToHashSet();
        var walk = TaxCostLedger.Walk(events, costed, RatesOfWalk(events, costed, rates ?? []), 2);
        return ThresholdTest.Compute(
            taxYear, walk, treatments, earlierTreatments, noThreshold ? null : threshold ?? FifThreshold.For(taxYear), 2);
    }
}
