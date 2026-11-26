using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.NetWorth;

/// <summary>Valuation-history R14, R15: the <c>net</c> of each currency of net worth now folded into the Tenant's
/// reporting currency at the stored rates of today (the platform's UTC date).</summary>
internal static class NetWorthFold
{
    public static async Task<FoldedTotalDto> FoldAsync(
        IFxRate fxRates,
        ICurrencyCatalog currencies,
        IReadOnlyList<NetWorthCurrencyDto> totals,
        string reportingCurrency,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var amounts = totals.ToDictionary(row => row.Currency, row => row.Net);
        var asOf = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rates = new Dictionary<string, StoredRate>();
        foreach (var currency in amounts.Keys.Where(code => code != reportingCurrency))
        {
            try
            {
                var reading = await fxRates.GetRateDetailAsync(currency, reportingCurrency, asOf, cancellationToken);
                rates[currency] = new StoredRate(reading.Rate, reading.AsOfDate, reading.Source.Trim());
            }
            catch (FxRateNotFoundException)
            {
                // R10: the line is NoRate; the read does not fail (fx-rates R7).
            }
        }

        int Places(string code) => currencies.TryGet(code)?.DecimalPlaces
                                   ?? throw new InvalidOperationException($"Currency {code} is not in the catalog.");

        var folded = FxFold.Compute(amounts, reportingCurrency, Places(reportingCurrency), rates, today);
        return new FoldedTotalDto(
            folded.Currency,
            decimal.Round(folded.Total, Places(reportingCurrency), MidpointRounding.AwayFromZero),
            folded.Complete,
            folded.Lines.Select(line => FoldLineDto.From(line, Places, reportingCurrency)).ToList());
    }
}
