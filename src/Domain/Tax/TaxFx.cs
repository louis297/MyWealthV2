using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Domain.Tax;

/// <summary>
/// Tax-year-events R18, R21 and tax-cost-threshold R14: one rule for every amount in NZD. A figure in NZD is used as
/// it is; any other needs the stored row of the directed pair into NZD read for the date, no older than the age limit.
/// </summary>
internal static class TaxFx
{
    public static (TaxAmountStatus Status, StoredRate? Rate) RateOf(
        string currency, DateOnly date, IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates)
    {
        if (currency == CustomerTaxYear.Nzd)
        {
            return (TaxAmountStatus.Stated, null);
        }

        if (!rates.TryGetValue((currency, date), out var rate))
        {
            return (TaxAmountStatus.NoRate, null);
        }

        return ValuationRules.IsFresh(rate.AsOfDate, date) ? (TaxAmountStatus.Converted, rate) : (TaxAmountStatus.StaleRate, rate);
    }

    /// <summary>The figure in NZD at <paramref name="status"/>, rounded to <paramref name="places"/>, or none.</summary>
    public static decimal? Nzd(decimal amount, TaxAmountStatus status, StoredRate? rate, int places) => status switch
    {
        TaxAmountStatus.Stated => Round(amount, places),
        TaxAmountStatus.Converted => Round(amount * rate!.Rate, places),
        _ => null
    };

    public static decimal Round(decimal amount, int places) => decimal.Round(amount, places, MidpointRounding.AwayFromZero);

    /// <summary>0 with <paramref name="places"/> decimal places, so that a zero is written as 0.00 for NZD.</summary>
    public static decimal Zero(int places) => new(0, 0, 0, false, (byte)places);
}
