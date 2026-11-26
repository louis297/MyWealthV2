using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>
/// The worked example of account-valuation §1.1: one NZD account, AAA in NZD, BBB and CCC in USD, from Monday 2 to
/// Wednesday 11 March 2026. The ledger is replayed by <c>bookedAt</c>; the price and rate rows are read as the stores
/// read them (the latest <c>AsOfDate</c> on or before the day).
/// </summary>
internal static class WorkedExample
{
    public const int Aaa = 1;
    public const int Bbb = 2;
    public const int Ccc = 3;

    public static readonly IReadOnlyDictionary<string, int> DecimalPlaces =
        new Dictionary<string, int> { ["NZD"] = 2, ["USD"] = 2, ["JPY"] = 0 };

    /// <summary>T5: the entitlement posting of the hand-entered BBB split 2 for 1, ex-date Thursday 5 March.</summary>
    public static readonly BasisChange BbbSplit = new(
        Bbb, Day(5), At(6, 12, 1), CorporateActionKind.Split, 2, 1);

    private static readonly (DateTimeOffset BookedAt, decimal Cash, int? Instrument, decimal Quantity)[] Ledger =
    [
        (At(-3, 9, 0), 10_000.00m, null, 0m),
        (At(-3, 9, 0), 0m, Aaa, 1_000m),
        (At(-3, 9, 0), 0m, Bbb, 50m),
        (At(-3, 9, 0), 0m, Ccc, 200m),
        (At(3, 20, 30), -1_250.00m - 15.00m, Aaa, 500m), // T1
        (At(4, 2, 0), 2_000.00m, null, 0m), // T2
        (At(5, 22, 10), 5_641.60m - 20.00m, Ccc, -80m), // T3
        (At(6, 1, 15), -2_000.00m, null, 0m), // T4
        (At(6, 12, 1), 0m, Bbb, 50m) // T5
    ];

    private static readonly Dictionary<int, (int Day, decimal Price)[]> PriceRows = new()
    {
        [Aaa] = [(2, 2.48m), (3, 2.50m), (4, 2.52m), (5, 2.47m), (6, 2.55m), (9, 2.60m), (10, 2.58m), (11, 2.61m)],
        [Bbb] = [(2, 180.00m), (3, 182.00m), (4, 184.00m), (5, 92.50m), (6, 93.10m), (9, 94.00m), (10, 93.40m), (11, 95.20m)],
        [Ccc] = [(2, 40.00m), (3, 40.50m), (4, 40.20m), (5, 41.00m), (6, 41.30m)]
    };

    private static readonly (int Day, decimal Rate)[] UsdNzd =
    [
        (2, 1.7100m), (3, 1.7150m), (4, 1.7180m), (5, 1.7200m), (6, 1.7250m), (7, 1.7250m), (8, 1.7260m),
        (9, 1.7300m), (10, 1.7280m), (11, 1.7310m)
    ];

    public static DateOnly Day(int day) => new(2026, 3, day);

    /// <summary>An instant in March 2026, UTC; a day below 1 counts back into February.</summary>
    public static DateTimeOffset At(int day, int hour, int minute) =>
        new DateTimeOffset(2026, 3, 1, hour, minute, 0, TimeSpan.Zero).AddDays(day - 1);

    public static string Currency(int instrument) => instrument == Aaa ? "NZD" : "USD";

    public static AccountPosition PositionOf(DateOnly day)
    {
        var end = LedgerDay.End(day);
        var booked = Ledger.Where(row => row.BookedAt < end).ToList();
        var lines = booked
            .Where(row => row.Instrument is not null)
            .GroupBy(row => row.Instrument!.Value)
            .Select(group => new PositionLine(group.Key, group.Sum(row => row.Quantity)))
            .Where(line => line.LedgerQuantity != 0m)
            .OrderBy(line => line.InstrumentId)
            .ToList();
        return new AccountPosition(day, booked.Sum(row => row.Cash), lines);
    }

    public static Dictionary<int, StoredPrice> PricesOn(DateOnly day)
    {
        var prices = new Dictionary<int, StoredPrice>();
        foreach (var (instrument, rows) in PriceRows)
        {
            var row = rows.Where(candidate => Day(candidate.Day) <= day).OrderBy(candidate => candidate.Day).LastOrDefault();
            if (row.Day != 0)
            {
                prices[instrument] = new StoredPrice(row.Price, Currency(instrument), Day(row.Day), "EODHD");
            }
        }

        return prices;
    }

    public static Dictionary<string, StoredRate> RatesOn(DateOnly day)
    {
        var row = UsdNzd.Where(candidate => Day(candidate.Day) <= day).OrderBy(candidate => candidate.Day).LastOrDefault();
        return row.Day == 0
            ? []
            : new Dictionary<string, StoredRate> { ["USD"] = new(row.Rate, Day(row.Day), "Frankfurter") };
    }

    public static AccountValuation ValueOn(DateOnly day, params BasisChange[] basisChanges) =>
        AccountValuation.Compute(PositionOf(day), "NZD", DecimalPlaces, PricesOn(day), RatesOn(day), basisChanges);
}
