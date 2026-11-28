using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>
/// The worked example of docs/features/income-summary.md §1.1: the Customer of tax-year-events §1.1 (T1 to T12, A1
/// Custodial in USD, A2 External in NZD, AAA) with two more Instruments held on A2, KIW and OZB, and the Transactions I1
/// to I5, D1 and D2, which have the internal ids 101 to 107.
/// </summary>
internal static class IncomeWorkedExample
{
    public const int Kiw = 11;
    public const int Ozb = 12;

    public static LedgerFact I1 => Fact(101, Utc(2025, 9, 30, 0, 0), TransactionType.Interest,
        [Cash(A2, CashLegRole.Principal, 120.00m), Cash(A2, CashLegRole.Tax, -21.00m, TaxKind.Rwt)], []);

    public static LedgerFact I2 => Fact(102, Utc(2025, 10, 15, 0, 0), TransactionType.Interest,
        [Cash(A2, CashLegRole.Principal, -8.00m)], []);

    public static LedgerFact D1 => Fact(103, Utc(2025, 12, 12, 0, 0), TransactionType.Dividend,
        [Cash(A2, CashLegRole.Principal, 720.00m), Cash(A2, CashLegRole.Tax, -50.00m, TaxKind.Rwt)],
        [],
        dividend: new LedgerDividend(Kiw, 1_000m, DividendTaxSource.NewZealand, 280.00m));

    public static LedgerFact I3 => Posting(104, Utc(2026, 2, 1, 0, 30), 4.10m, 1.35m,
        new LedgerInterest(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 31));

    public static LedgerFact I4 => Posting(105, Utc(2026, 3, 1, 0, 30), 3.80m, 1.25m,
        new LedgerInterest(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), 28));

    public static LedgerFact D2 => Fact(106, Utc(2026, 3, 20, 0, 0), TransactionType.Dividend,
        [Cash(A2, CashLegRole.Principal, 327.00m), Cash(A2, CashLegRole.Tax, -49.05m, TaxKind.ForeignWithholding)],
        [],
        dividend: new LedgerDividend(Ozb, 200m, DividendTaxSource.Foreign, 0m));

    public static LedgerFact I5 => Posting(107, Utc(2026, 4, 1, 0, 30), 4.30m, 1.42m,
        new LedgerInterest(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 31));

    /// <summary>T1 to T12 of tax-year-events §1.1 and the seven Transactions of this example.</summary>
    public static IReadOnlyList<LedgerFact> Facts => [.. TaxWorkedExample.Facts, I1, I2, D1, I3, I4, D2, I5];

    /// <summary>The rates of tax-year-events §1.1 and the two of this example.</summary>
    public static IReadOnlyList<StoredRate> Rates => [.. UsdRates, Rate(2026, 1, 30, 1.7200m), Rate(2026, 2, 27, 1.7500m)];

    /// <summary>AAA as tax-year-events §1.1; KIW <c>NotForeign</c>, NZ, from 2025; OZB <c>ExemptAsxListed</c>, AU, for 2026.</summary>
    public static IReadOnlyDictionary<int, TaxTreatment> Treatments => new Dictionary<int, TaxTreatment>
    {
        [Aaa] = new(FifTreatment.Attributing, 2025, null, "US"),
        [Kiw] = new(FifTreatment.NotForeign, 2025, null, "NZ"),
        [Ozb] = new(FifTreatment.ExemptAsxListed, 2026, null, "AU")
    };

    /// <summary>An interest run's posting on A1: <c>+gross</c> and an RWT leg of <c>−tax</c>, with its period.</summary>
    public static LedgerFact Posting(int id, DateTimeOffset bookedAt, decimal gross, decimal rwt, LedgerInterest? period) =>
        new(id, IdOf(id), bookedAt, TransactionType.Interest, false,
            [Cash(A1, CashLegRole.Principal, gross), Cash(A1, CashLegRole.Tax, -rwt, TaxKind.Rwt)], [], Interest: period);

    /// <summary>
    /// What Application reads: the USD row with the latest date on or before the UTC date of each event's and each
    /// interest item's <c>bookedAt</c>, and of an arrival's <c>acquiredOn</c>.
    /// </summary>
    public static IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> RatesFor(
        IEnumerable<TaxEvent> events, IEnumerable<TaxInterest> interest, IReadOnlyList<StoredRate> rows)
    {
        var rates = new Dictionary<(string Currency, DateOnly Date), StoredRate>(TaxWorkedExample.RatesFor(events, rows));
        foreach (var date in interest.Where(item => item.Currency == "USD").Select(item => LedgerDay.Of(item.BookedAt)).Distinct())
        {
            if (rows.Where(row => row.AsOfDate <= date).MaxBy(row => row.AsOfDate) is { } row)
            {
                rates[("USD", date)] = row;
            }
        }

        return rates;
    }

    /// <summary>The example's tax year <paramref name="taxYear"/>, with the rates and the treatments of §1.1.</summary>
    public static CustomerTaxYear Year(
        int taxYear,
        IReadOnlyList<LedgerFact>? facts = null,
        IReadOnlyList<StoredRate>? rates = null,
        IReadOnlyDictionary<int, TaxTreatment>? treatments = null)
    {
        var list = facts ?? Facts;
        var events = TaxLedger.Build(list);
        var interest = TaxLedger.BuildInterest(list);
        return CustomerTaxYear.Compute(
            taxYear,
            events,
            treatments ?? Treatments,
            RatesFor(events, interest, rates ?? Rates),
            TaxYearValues.None,
            2,
            interest: interest);
    }
}
