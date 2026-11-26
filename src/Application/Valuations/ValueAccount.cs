using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Valuations;

/// <summary>The one place a valuation is worked out (account-valuation §7).</summary>
public interface IValueAccount
{
    Task<AccountValuationRead> ForDayAsync(Account account, DateOnly day, CancellationToken cancellationToken);
}

/// <summary>A valuation with the Instruments of its lines.</summary>
public sealed record AccountValuationRead(AccountValuation Valuation, IReadOnlyDictionary<int, ValuedInstrument> Instruments);

/// <summary>An Instrument of a valuation line as the item shows it.</summary>
public sealed record ValuedInstrument(int Id, Guid PublicId, string Symbol, string Name, string QuoteCurrency);

/// <summary>
/// Account valuation R9–R12: the position of the day, the stored price of each line and the stored rate of each
/// quote currency as of the day, the decimal places from the catalog, then <see cref="AccountValuation.Compute"/>.
/// Stored rows only; no source is called. A missing rate is passed on as none, never as a number (fx-rates R7).
/// </summary>
public class ValueAccount(
    IApplicationDbContext db,
    IAccountPositions positions,
    IMarketData marketData,
    IFxRate fxRates,
    ICurrencyCatalog currencies) : IValueAccount
{
    public async Task<AccountValuationRead> ForDayAsync(Account account, DateOnly day, CancellationToken cancellationToken)
    {
        var read = await positions.ReadAsync(account, day, cancellationToken);
        var ids = read.Position.Lines.Select(line => line.InstrumentId).ToList();
        var instruments = (await db.Instruments.IgnoreQueryFilters().AsNoTracking()
                .Where(instrument => ids.Contains(instrument.Id))
                .Select(instrument => new { instrument.Id, instrument.PublicId, instrument.Symbol, instrument.Name, instrument.QuoteCurrency })
                .ToListAsync(cancellationToken))
            .ToDictionary(
                row => row.Id,
                row => new ValuedInstrument(row.Id, row.PublicId, row.Symbol, row.Name, Code(row.QuoteCurrency)));

        // R9, R10: the row with the latest AsOfDate on or before the day.
        var asOf = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var prices = new Dictionary<int, StoredPrice>();
        foreach (var id in ids)
        {
            if (marketData.TryGetPriceDetail(id, asOf) is { } reading)
            {
                prices[id] = new StoredPrice(
                    reading.Price.Amount, Code(reading.Price.Currency), reading.AsOfDate, reading.Source.Trim());
            }
        }

        var accountCurrency = Code(account.Currency);
        var quotes = instruments.Values.Select(instrument => instrument.QuoteCurrency).Distinct().ToList();
        var rates = new Dictionary<string, StoredRate>();
        foreach (var quote in quotes.Where(quote => quote != accountCurrency))
        {
            try
            {
                var reading = await fxRates.GetRateDetailAsync(quote, accountCurrency, asOf, cancellationToken);
                rates[quote] = new StoredRate(reading.Rate, reading.AsOfDate, reading.Source.Trim());
            }
            catch (FxRateNotFoundException)
            {
                // R14: the line is NoRate.
            }
        }

        var decimalPlaces = quotes.Append(accountCurrency).Distinct().ToDictionary(
            code => code,
            code => currencies.TryGet(code)?.DecimalPlaces
                    ?? throw new InvalidOperationException($"Currency {code} is not in the catalog."));

        var valuation = AccountValuation.Compute(
            read.Position, accountCurrency, decimalPlaces, prices, rates, read.BasisChanges);
        return new AccountValuationRead(valuation, instruments);
    }

    private static string Code(string currency) => currency.Trim().ToUpperInvariant();
}
