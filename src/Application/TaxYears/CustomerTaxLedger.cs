using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.TaxYears;

/// <summary>The one place a tax year is worked out (tax-year-events §7).</summary>
public interface ICustomerTaxLedger
{
    Task<CustomerTaxLedgerRead> ReadAsync(int tenantId, int customerId, int taxYear, CancellationToken cancellationToken);
}

/// <summary>A tax year with the Accounts and the Instruments its ids name.</summary>
public sealed record CustomerTaxLedgerRead(
    CustomerTaxYear Year,
    IReadOnlyDictionary<int, Guid> AccountIds,
    IReadOnlyDictionary<int, ValuedInstrument> Instruments);

/// <summary>
/// Tax-year-events §7: the Customer's Accounts, the facts of R6, <see cref="TaxLedger.Build"/>, the rate rows the events
/// of the year need, the values of the opening, the closing and each arrival or departure through
/// <see cref="IValueAccount.ForPositionAsync"/>, the treatments, then <see cref="CustomerTaxYear.Compute"/>. Reads ignore
/// the tenant filter and name the Tenant themselves, so that a <c>worker</c> run can call this later.
/// </summary>
public class CustomerTaxLedger(
    IApplicationDbContext db,
    IAccountPositions positions,
    IValueAccount valueAccount,
    IFxRate fxRates,
    ICurrencyCatalog currencies) : ICustomerTaxLedger
{
    public async Task<CustomerTaxLedgerRead> ReadAsync(int tenantId, int customerId, int taxYear, CancellationToken cancellationToken)
    {
        var from = TaxYear.Start(taxYear);
        var to = TaxYear.End(taxYear);

        // R5: every Account of the Customer, of both custody modes and any status.
        var accounts = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .Where(account => account.TenantId == tenantId && account.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        var byId = accounts.ToDictionary(account => account.Id);

        var facts = await FactsAsync(tenantId, byId, to, cancellationToken);
        var events = TaxLedger.Build(facts);

        var instrumentIds = events.Select(e => e.InstrumentId).Distinct().ToList();
        var instruments = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Include(instrument => instrument.FifTreatments)
            .Where(instrument => instrument.TenantId == tenantId && instrumentIds.Contains(instrument.Id))
            .ToListAsync(cancellationToken);
        var treatments = instruments.ToDictionary(instrument => instrument.Id, instrument => Treatment(instrument, taxYear));

        var ofYear = events.Where(e => e.BookedAt >= from && e.BookedAt < to).ToList();
        var rates = await RatesAsync(ofYear, cancellationToken);

        var basisChanges = new Dictionary<int, IReadOnlyList<BasisChange>>();
        var valued = treatments.Where(row => CustomerTaxYear.NeedsValue(row.Value.FifTreatment)).Select(row => row.Key).ToHashSet();
        var values = new TaxYearValues(
            await LinesAtAsync(events, from, TaxYear.OpeningDay(taxYear), valued, byId, basisChanges, cancellationToken),
            await LinesAtAsync(events, to, TaxYear.ClosingDay(taxYear), valued, byId, basisChanges, cancellationToken),
            await MarketValuesAsync(ofYear, byId, cancellationToken));

        var year = CustomerTaxYear.Compute(taxYear, events, treatments, rates, values, Places(CustomerTaxYear.Nzd));
        return new CustomerTaxLedgerRead(
            year,
            accounts.ToDictionary(account => account.Id, account => account.PublicId),
            instruments.ToDictionary(
                instrument => instrument.Id,
                instrument => new ValuedInstrument(
                    instrument.Id, instrument.PublicId, instrument.Symbol, instrument.Name, Code(instrument.QuoteCurrency))));
    }

    /// <summary>
    /// R6, R7: every Transaction with a security leg on one of the Accounts, and every <c>Dividend</c> with a cash leg on
    /// one of them, booked before <paramref name="end"/>; a reversed one and a Reversal are not read.
    /// </summary>
    private async Task<List<LedgerFact>> FactsAsync(
        int tenantId, IReadOnlyDictionary<int, Account> accounts, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var accountIds = accounts.Keys.ToList();
        var transactions = db.Transactions.IgnoreQueryFilters()
            .Where(transaction => transaction.TenantId == tenantId
                                  && transaction.BookedAt < end
                                  && transaction.ReversedByTransactionId == null
                                  && transaction.Type != TransactionType.Reversal);

        var securityLegs = await (
                from leg in db.TransactionSecurityLegs.IgnoreQueryFilters()
                join transaction in transactions on leg.TransactionId equals transaction.Id
                where leg.TenantId == tenantId && accountIds.Contains(leg.AccountId)
                select new { leg.Id, leg.TransactionId, leg.AccountId, leg.InstrumentId, leg.Quantity, leg.CostAmount, leg.CostCurrency })
            .ToListAsync(cancellationToken);

        var tradeTypes = new[] { TransactionType.Buy, TransactionType.Sell, TransactionType.Dividend };
        var cashLegs = await (
                from leg in db.TransactionCashLegs.IgnoreQueryFilters()
                join transaction in transactions on leg.TransactionId equals transaction.Id
                join tax in db.TransactionTaxDetails.IgnoreQueryFilters() on leg.Id equals tax.CashLegId into taxes
                from tax in taxes.DefaultIfEmpty()
                where leg.TenantId == tenantId && accountIds.Contains(leg.AccountId) && tradeTypes.Contains(transaction.Type)
                select new { leg.TransactionId, leg.AccountId, leg.Role, leg.Amount, leg.Currency, Kind = (TaxKind?)tax.Kind })
            .ToListAsync(cancellationToken);

        var ids = securityLegs.Select(leg => leg.TransactionId)
            .Concat(cashLegs.Select(leg => leg.TransactionId))
            .Distinct()
            .ToList();
        var headers = await transactions
            .Where(transaction => ids.Contains(transaction.Id))
            .Select(transaction => new
            {
                transaction.Id,
                transaction.PublicId,
                transaction.BookedAt,
                transaction.Type,
                transaction.ReversedByTransactionId,
                InKind = transaction.InKindTransferDetail == null
                    ? null
                    : new { transaction.InKindTransferDetail.ChangesBeneficialOwner, transaction.InKindTransferDetail.AcquiredOn }
            })
            .ToListAsync(cancellationToken);
        var dividends = await db.TransactionDividendDetails.IgnoreQueryFilters()
            .Where(detail => detail.TenantId == tenantId && ids.Contains(detail.TransactionId))
            .Select(detail => new { detail.TransactionId, detail.InstrumentId, detail.Quantity, detail.TaxSource, detail.ImputationCredit })
            .ToDictionaryAsync(detail => detail.TransactionId, cancellationToken);

        var securityByTransaction = securityLegs.ToLookup(leg => leg.TransactionId);
        var cashByTransaction = cashLegs.ToLookup(leg => leg.TransactionId);
        return headers
            .Where(header => header.Type != TransactionType.Dividend || dividends.ContainsKey(header.Id))
            .Select(header => new LedgerFact(
                header.Id,
                header.PublicId,
                header.BookedAt,
                header.Type,
                header.ReversedByTransactionId is not null,
                cashByTransaction[header.Id]
                    .Select(leg => new LedgerCashLeg(leg.AccountId, accounts[leg.AccountId].CustodyMode, leg.Role, leg.Amount, Code(leg.Currency), leg.Kind))
                    .ToList(),
                securityByTransaction[header.Id]
                    .Select(leg => new LedgerSecurityLeg(
                        leg.Id, leg.AccountId, accounts[leg.AccountId].CustodyMode, leg.InstrumentId, leg.Quantity, leg.CostAmount, Code(leg.CostCurrency)))
                    .ToList(),
                dividends.TryGetValue(header.Id, out var dividend)
                    ? new LedgerDividend(dividend.InstrumentId, dividend.Quantity, dividend.TaxSource, dividend.ImputationCredit)
                    : null,
                header.InKind is null ? null : new LedgerInKindTransfer(header.InKind.ChangesBeneficialOwner, header.InKind.AcquiredOn)))
            .ToList();
    }

    /// <summary>R26: the treatment for the year and what a report keeps of it.</summary>
    private static TaxTreatment Treatment(Instrument instrument, int taxYear) =>
        instrument.FifTreatmentRowFor(taxYear) is { } row
            ? new TaxTreatment(row.Treatment, row.TaxYear, row.Note, instrument.IssuerCountry)
            : TaxTreatment.NotClassified(instrument.IssuerCountry);

    /// <summary>
    /// R18, R21: for each currency other than NZD and each UTC date of a <c>bookedAt</c>, the stored row of the pair into
    /// NZD with the latest date on or before it. A missing pair is passed on as none, never as a number (fx-rates R7).
    /// </summary>
    private async Task<Dictionary<(string Currency, DateOnly Date), StoredRate>> RatesAsync(
        IEnumerable<TaxEvent> events, CancellationToken cancellationToken)
    {
        var rates = new Dictionary<(string Currency, DateOnly Date), StoredRate>();
        var keys = events
            .Select(e => (Currency: e.Trade?.Currency ?? e.Dividend?.Currency, Date: LedgerDay.Of(e.BookedAt)))
            .Where(key => key.Currency is { Length: > 0 } && key.Currency != CustomerTaxYear.Nzd)
            .Distinct();
        foreach (var (currency, date) in keys)
        {
            try
            {
                var asOf = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var reading = await fxRates.GetRateDetailAsync(currency!, CustomerTaxYear.Nzd, asOf, cancellationToken);
                rates[(currency!, date)] = new StoredRate(reading.Rate, reading.AsOfDate, reading.Source.Trim());
            }
            catch (FxRateNotFoundException)
            {
                // R21: the event is NoRate.
            }
        }

        return rates;
    }

    /// <summary>
    /// R22: each Account's quantity at <paramref name="at"/> of the Instruments that need a value (R23), valued as of
    /// <paramref name="day"/> in NZD with the Account's basis changes and the position cut at the instant.
    /// </summary>
    private async Task<List<TaxValueLine>> LinesAtAsync(
        IReadOnlyList<TaxEvent> events,
        DateTimeOffset at,
        DateOnly day,
        IReadOnlySet<int> valued,
        IReadOnlyDictionary<int, Account> accounts,
        Dictionary<int, IReadOnlyList<BasisChange>> basisChanges,
        CancellationToken cancellationToken)
    {
        var lines = new List<TaxValueLine>();
        foreach (var group in TaxLedger.QuantitiesAt(events, at)
                     .Where(quantity => valued.Contains(quantity.InstrumentId))
                     .GroupBy(quantity => quantity.AccountId))
        {
            var account = accounts[group.Key];
            if (!basisChanges.TryGetValue(account.Id, out var changes))
            {
                changes = await positions.BasisChangesAsync(account, cancellationToken);
                basisChanges[account.Id] = changes;
            }

            var position = new AccountPosition(day, 0m, group.Select(q => new PositionLine(q.InstrumentId, q.Quantity)).ToList(), at);
            var read = await valueAccount.ForPositionAsync(account, position, CustomerTaxYear.Nzd, changes, cancellationToken);
            lines.AddRange(read.Valuation.Lines.Select(line => new TaxValueLine(account.Id, account.CustodyMode, line)));
        }

        return lines;
    }

    /// <summary>R14: an arrival or a departure of the year, as a position of that one line on the UTC date of its
    /// <c>bookedAt</c>, in NZD, with no basis change.</summary>
    private async Task<Dictionary<int, ValuationLine>> MarketValuesAsync(
        IEnumerable<TaxEvent> events, IReadOnlyDictionary<int, Account> accounts, CancellationToken cancellationToken)
    {
        var lines = new Dictionary<int, ValuationLine>();
        foreach (var taxEvent in events.Where(e => e.Kind is TaxEventKind.Arrival or TaxEventKind.Departure))
        {
            var day = LedgerDay.Of(taxEvent.BookedAt);
            var position = new AccountPosition(day, 0m, [new PositionLine(taxEvent.InstrumentId, Math.Abs(taxEvent.Quantity))]);
            var read = await valueAccount.ForPositionAsync(
                accounts[taxEvent.AccountId], position, CustomerTaxYear.Nzd, [], cancellationToken);
            lines[taxEvent.Ordinal] = read.Valuation.Lines.Single();
        }

        return lines;
    }

    private int Places(string currency) =>
        currencies.TryGet(currency)?.DecimalPlaces
        ?? throw new InvalidOperationException($"Currency {currency} is not in the catalog.");

    private static string Code(string currency) => currency.Trim().ToUpperInvariant();
}
