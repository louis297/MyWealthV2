using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>
/// The worked example of docs/features/tax-year-events.md §1.1. One Customer with two Accounts: A1, Custodial, in USD;
/// A2, External, in NZD. One Instrument, AAA, quoted in USD. The Transactions T1 to T12 have the internal ids 1 to 12.
/// </summary>
internal static class TaxWorkedExample
{
    public const int A1 = 1;
    public const int A2 = 2;
    public const int Aaa = 10;

    public static readonly IReadOnlyDictionary<string, int> DecimalPlaces = new Dictionary<string, int> { ["NZD"] = 2, ["USD"] = 2 };

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    public static Guid IdOf(int transactionId) => new($"00000000-0000-0000-0000-{transactionId:D12}");

    public static LedgerFact T1 => Fact(1, Utc(2025, 3, 10, 21, 0), TransactionType.Buy,
        [Cash(A1, CashLegRole.Principal, -5_000.00m), Cash(A1, CashLegRole.Fee, -5.00m)], [Security(1, A1, 100m, 5_005.00m)]);

    public static LedgerFact T2 => Fact(2, Utc(2025, 3, 31, 9, 30), TransactionType.Buy,
        [Cash(A2, CashLegRole.Principal, -3_640.00m), Cash(A2, CashLegRole.Fee, -7.00m)], [Security(2, A2, 40m, 2_084.00m)]);

    public static LedgerFact T3 => Fact(3, Utc(2025, 3, 31, 20, 15), TransactionType.Sell,
        [Cash(A1, CashLegRole.Principal, 1_590.00m), Cash(A1, CashLegRole.Fee, -5.00m)], [Security(3, A1, -30m, -1_501.50m)]);

    public static LedgerFact T4 => Fact(4, Utc(2025, 5, 20, 2, 10), TransactionType.Dividend,
        [
            Cash(A1, CashLegRole.Principal, 35.00m),
            Cash(A1, CashLegRole.Tax, -5.25m, TaxKind.ForeignWithholding),
            Cash(A1, CashLegRole.Tax, -6.30m, TaxKind.Rwt)
        ],
        [],
        dividend: new LedgerDividend(Aaa, 70m, DividendTaxSource.Foreign, 0m));

    public static LedgerFact T5 => Fact(5, Utc(2025, 6, 3, 4, 0), TransactionType.Buy,
        [Cash(A2, CashLegRole.Principal, -48_125.00m)], [Security(5, A2, 500m, 27_500.00m)], reversed: true);

    public static LedgerFact T6 => Fact(6, Utc(2025, 6, 5, 1, 0), TransactionType.Reversal,
        [Cash(A2, CashLegRole.Principal, 48_125.00m)], [Security(6, A2, -500m, -27_500.00m)]);

    public static LedgerFact T7 => Fact(7, Utc(2025, 7, 1, 0, 0), TransactionType.Split, [], [Security(7, A2, 40m, 0m)]);

    public static LedgerFact T8 => Fact(8, Utc(2025, 7, 2, 12, 1), TransactionType.Split, [], [Security(8, A1, 70m, 0m)]);

    public static LedgerFact T9 => Fact(9, Utc(2025, 9, 15, 2, 30), TransactionType.Dividend,
        [
            Cash(A1, CashLegRole.Principal, 35.00m),
            Cash(A1, CashLegRole.Tax, -5.25m, TaxKind.ForeignWithholding),
            Cash(A1, CashLegRole.Tax, -6.30m, TaxKind.Rwt),
            Cash(A1, CashLegRole.Reinvestment, -22.40m)
        ],
        [Security(9, A1, 0.8m, 22.40m)],
        dividend: new LedgerDividend(Aaa, 140m, DividendTaxSource.Foreign, 0m));

    public static LedgerFact T10 => Fact(10, Utc(2025, 11, 10, 0, 0), TransactionType.StockOut, [], [Security(10, A2, -80m, -4_169.00m)]);

    public static LedgerFact T11 => Fact(11, Utc(2025, 11, 14, 3, 20), TransactionType.StockIn, [], [Security(11, A1, 80m, 4_169.00m)],
        inKind: new LedgerInKindTransfer(false, null));

    public static LedgerFact T12 => Fact(12, Utc(2026, 2, 2, 21, 40), TransactionType.Buy,
        [Cash(A1, CashLegRole.Principal, -600.00m), Cash(A1, CashLegRole.Fee, -5.00m)], [Security(12, A1, 20m, 605.00m)]);

    public static IReadOnlyList<LedgerFact> Facts => [T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12];

    /// <summary>The stored rates USD → NZD of §1.1.</summary>
    public static IReadOnlyList<StoredRate> UsdRates =>
    [
        Rate(2025, 3, 10, 1.7400m),
        Rate(2025, 3, 31, 1.7600m),
        Rate(2025, 5, 20, 1.6900m),
        Rate(2025, 9, 15, 1.6800m),
        Rate(2026, 2, 2, 1.7300m),
        Rate(2026, 3, 31, 1.7450m)
    ];

    /// <summary>AAA: issuer country US, Attributing from tax year 2025.</summary>
    public static IReadOnlyDictionary<int, TaxTreatment> Treatments => new Dictionary<int, TaxTreatment>
    {
        [Aaa] = new(FifTreatment.Attributing, 2025, null, "US")
    };

    /// <summary>What Application reads for each event: the USD row with the latest date on or before the UTC date of
    /// its <c>bookedAt</c>.</summary>
    public static IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> RatesFor(
        IEnumerable<TaxEvent> events, IReadOnlyList<StoredRate> rows)
    {
        var rates = new Dictionary<(string Currency, DateOnly Date), StoredRate>();
        foreach (var date in events.Select(e => LedgerDay.Of(e.BookedAt)).Distinct())
        {
            if (rows.Where(row => row.AsOfDate <= date).MaxBy(row => row.AsOfDate) is { } row)
            {
                rates[("USD", date)] = row;
            }
        }

        return rates;
    }

    /// <summary>The worked example's tax year <paramref name="taxYear"/>, with the rates of §1.1.</summary>
    public static CustomerTaxYear Year(
        int taxYear,
        IReadOnlyList<LedgerFact>? facts = null,
        IReadOnlyList<StoredRate>? rates = null,
        TaxYearValues? values = null,
        IReadOnlyDictionary<int, TaxTreatment>? treatments = null)
    {
        var events = TaxLedger.Build(facts ?? Facts);
        return CustomerTaxYear.Compute(
            taxYear, events, treatments ?? Treatments, RatesFor(events, rates ?? UsdRates), values ?? TaxYearValues.None, 2);
    }

    /// <summary>The stored prices of AAA in §1.1.</summary>
    public static IReadOnlyList<StoredPrice> AaaPrices => [Price(2025, 3, 31, 53.20m), Price(2026, 3, 31, 31.00m)];

    /// <summary>
    /// What Application values for the opening and the closing of <paramref name="taxYear"/>: each Account's
    /// quantity at the instant, valued as of 31 March in NZD by <see cref="AccountValuation.Compute"/>.
    /// </summary>
    public static TaxYearValues ValuesOf(
        int taxYear,
        IReadOnlyList<TaxEvent> events,
        IReadOnlyList<StoredPrice>? prices = null,
        IReadOnlyList<StoredRate>? rates = null,
        IReadOnlyDictionary<int, ValuationLine>? eventLines = null) =>
        new(
            LinesAt(TaxYear.Start(taxYear), TaxYear.OpeningDay(taxYear), events, prices ?? AaaPrices, rates ?? UsdRates),
            LinesAt(TaxYear.End(taxYear), TaxYear.ClosingDay(taxYear), events, prices ?? AaaPrices, rates ?? UsdRates),
            eventLines ?? new Dictionary<int, ValuationLine>());

    public static IReadOnlyList<TaxValueLine> LinesAt(
        DateTimeOffset at, DateOnly day, IReadOnlyList<TaxEvent> events, IReadOnlyList<StoredPrice> prices, IReadOnlyList<StoredRate> rates)
    {
        var lines = new List<TaxValueLine>();
        foreach (var account in TaxLedger.QuantitiesAt(events, at).GroupBy(quantity => (quantity.AccountId, quantity.CustodyMode)))
        {
            var position = new AccountPosition(day, 0m, account.Select(q => new PositionLine(q.InstrumentId, q.Quantity)).ToList(), at);
            var price = prices.Where(row => row.AsOfDate <= day).MaxBy(row => row.AsOfDate);
            var rate = rates.Where(row => row.AsOfDate <= day).MaxBy(row => row.AsOfDate);
            var valuation = AccountValuation.Compute(
                position,
                "NZD",
                DecimalPlaces,
                price is null ? new Dictionary<int, StoredPrice>() : account.ToDictionary(q => q.InstrumentId, _ => price),
                rate is null ? new Dictionary<string, StoredRate>() : new Dictionary<string, StoredRate> { ["USD"] = rate },
                []);
            lines.AddRange(valuation.Lines.Select(line => new TaxValueLine(account.Key.AccountId, account.Key.CustodyMode, line)));
        }

        return lines;
    }

    /// <summary>The worked example's tax year with its opening and closing valued.</summary>
    public static CustomerTaxYear ValuedYear(int taxYear, IReadOnlyList<LedgerFact>? facts = null, IReadOnlyDictionary<int, TaxTreatment>? treatments = null)
    {
        var events = TaxLedger.Build(facts ?? Facts);
        return Year(taxYear, facts, values: ValuesOf(taxYear, events), treatments: treatments);
    }

    public static StoredRate Rate(int year, int month, int day, decimal rate) => new(rate, new DateOnly(year, month, day), "Frankfurter");

    public static StoredPrice Price(int year, int month, int day, decimal amount) => new(amount, "USD", new DateOnly(year, month, day), "EODHD");

    public static CustodyMode ModeOf(int accountId) => accountId == A1 ? CustodyMode.Custodial : CustodyMode.External;

    public static string CurrencyOf(int accountId) => accountId == A1 ? "USD" : "NZD";

    public static LedgerFact Fact(
        int id,
        DateTimeOffset bookedAt,
        TransactionType type,
        IReadOnlyList<LedgerCashLeg> cash,
        IReadOnlyList<LedgerSecurityLeg> security,
        bool reversed = false,
        LedgerDividend? dividend = null,
        LedgerInKindTransfer? inKind = null) =>
        new(id, IdOf(id), bookedAt, type, reversed, cash, security, dividend, inKind);

    public static LedgerCashLeg Cash(int accountId, CashLegRole role, decimal amount, TaxKind? taxKind = null) =>
        new(accountId, ModeOf(accountId), role, amount, CurrencyOf(accountId), taxKind);

    public static LedgerSecurityLeg Security(int legId, int accountId, decimal quantity, decimal cost, int instrumentId = Aaa) =>
        new(legId, accountId, ModeOf(accountId), instrumentId, quantity, cost, "USD");
}
