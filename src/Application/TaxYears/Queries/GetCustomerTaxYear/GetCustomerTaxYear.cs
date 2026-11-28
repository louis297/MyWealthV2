using System.Globalization;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.TaxYears.Queries.GetCustomerTaxYear;

/// <summary>Tax-year-events R1, R4, R30–R32: one Customer's tax events and holdings of one tax year, in NZD; with the
/// interest items and the income summary (income-summary R1, R13).</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetCustomerTaxYearQuery(Guid CustomerId, string? TaxYear) : IRequest<CustomerTaxYearDto>;

/// <summary>R4, R30: a whole number, from 2009, of a tax year that has ended and whose 31 March is final.</summary>
public class GetCustomerTaxYearQueryValidator : AbstractValidator<GetCustomerTaxYearQuery>
{
    public GetCustomerTaxYearQueryValidator(TimeProvider time)
    {
        RuleFor(query => query.TaxYear)
            .Cascade(CascadeMode.Stop)
            .Must(taxYear => Parse(taxYear) is not null).WithMessage("TaxYear must be a whole number.")
            .Must(taxYear => Parse(taxYear) >= Domain.Tax.TaxYear.FirstReadable).WithMessage("TaxYear must be 2009 or later.")
            .Must(taxYear => Domain.Tax.TaxYear.IsReadable(Parse(taxYear)!.Value, LedgerDay.Of(time.GetUtcNow())))
            .WithMessage(query => $"Tax year {Parse(query.TaxYear)} can be read from 2 April {Parse(query.TaxYear)}.");
    }

    public static int? Parse(string? taxYear) =>
        int.TryParse(taxYear, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var year) ? year : null;
}

public class GetCustomerTaxYearQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICustomerTaxLedger ledger,
    TimeProvider time) : IRequestHandler<GetCustomerTaxYearQuery, CustomerTaxYearDto>
{
    public async Task<CustomerTaxYearDto> Handle(GetCustomerTaxYearQuery request, CancellationToken cancellationToken)
    {
        var customer = await AccountScope.ReadableCustomerAsync(db, currentUser, request.CustomerId, cancellationToken);
        var taxYear = GetCustomerTaxYearQueryValidator.Parse(request.TaxYear)!.Value;

        // R31: the platform time the ledger was read.
        var readAt = time.GetUtcNow();
        var read = await ledger.ReadAsync(customer.TenantId!.Value, customer.Id, taxYear, cancellationToken);
        var year = read.Year;
        var holdings = year.Holdings
            .Select(holding => Holding(holding, read))
            .OrderBy(holding => holding.Instrument.Symbol, StringComparer.Ordinal)
            .ThenBy(holding => holding.Instrument.Id)
            .ToList();
        return new CustomerTaxYearDto(
            customer.PublicId, year.TaxYear, year.From, year.To, CustomerTaxYear.Nzd, readAt, holdings, year.Complete, year.IncompleteCount,
            Threshold(year.Threshold, read),
            Income(year.FifIncome, read),
            year.Interest.Select(item => InterestItem(item, read)).ToList(),
            IncomeSummary(year.Income, read));
    }

    /// <summary>Income-summary R3, R13: an interest item with PublicIds.</summary>
    private static TaxInterestItemDto InterestItem(TaxYearInterest item, CustomerTaxLedgerRead read)
    {
        var booked = item.Item;
        return new TaxInterestItemDto(
            item.Sequence,
            booked.BookedAt,
            booked.TransactionPublicId,
            read.AccountIds[booked.AccountId],
            booked.CustodyMode.ToString(),
            item.Amount,
            item.Status.ToString(),
            new TaxInterestLedgerDto(booked.Currency, booked.Gross, booked.Rwt, booked.Nrwt, booked.ForeignTax),
            Fx(item.Fx),
            item.Rwt,
            item.Nrwt,
            item.ForeignTax,
            booked.Period is { } period ? new TaxInterestPeriodDto(period.PeriodFrom, period.PeriodTo, period.Days) : null);
    }

    /// <summary>
    /// Income-summary R13: the income summary with PublicIds. The holding rows of a group are in the order of the read's
    /// holdings: by symbol, then by the Instrument's PublicId.
    /// </summary>
    private static IncomeSummaryDto IncomeSummary(TaxYearIncome income, CustomerTaxLedgerRead read)
    {
        Guid? InstrumentId(int? id) => id is { } value ? read.Instruments[value].PublicId : null;
        Guid? AccountId(int? id) => id is { } value ? read.AccountIds[value] : null;
        return new IncomeSummaryDto(
            new IncomeDividendsDto(
                income.Dividends.Groups
                    .Select(group => new IncomeGroupDto(
                        group.Group.ToString(),
                        group.Holdings
                            .OrderBy(row => read.Instruments[row.InstrumentId].Symbol, StringComparer.Ordinal)
                            .ThenBy(row => read.Instruments[row.InstrumentId].PublicId)
                            .Select(row => new IncomeHoldingRowDto(
                                read.Instruments[row.InstrumentId].PublicId, row.IssuerCountry, row.Sum.Count, row.Sum.Gross,
                                row.Sum.ImputationCredit, row.Sum.GrossWithImputation, row.Sum.ForeignTax, row.Sum.Rwt, row.Sum.Nrwt))
                            .ToList(),
                        group.Countries
                            .Select(row => new IncomeCountryRowDto(
                                row.IssuerCountry, row.Sum.Count, row.Sum.Gross, row.Sum.ImputationCredit, row.Sum.GrossWithImputation,
                                row.Sum.ForeignTax, row.Sum.Rwt, row.Sum.Nrwt))
                            .ToList(),
                        DividendSum(group.Total)))
                    .ToList(),
                DividendSum(income.Dividends.Total)),
            new IncomeInterestDto(
                income.Interest.Accounts
                    .Select(row => new IncomeAccountRowDto(
                        read.AccountIds[row.AccountId], row.CustodyMode.ToString(), row.Currency, row.Sum.Count, row.Sum.Received,
                        row.Sum.Charged, row.Sum.Rwt, row.Sum.Nrwt, row.Sum.ForeignTax))
                    .ToList(),
                new IncomeInterestSumDto(
                    income.Interest.Total.Count, income.Interest.Total.Received, income.Interest.Total.Charged, income.Interest.Total.Rwt,
                    income.Interest.Total.Nrwt, income.Interest.Total.ForeignTax)),
            new IncomeTaxWithheldDto(income.TaxWithheld.Rwt, income.TaxWithheld.Nrwt, income.TaxWithheld.ForeignTax),
            income.ImputationCredit,
            income.Complete,
            income.Incomplete.Select(reason => new IncomeIncompleteDto(reason.Reason.ToString(), InstrumentId(reason.InstrumentId), AccountId(reason.AccountId))).ToList(),
            income.Notices.Select(notice => new IncomeNoticeDto(notice.Notice.ToString(), InstrumentId(notice.InstrumentId), AccountId(notice.AccountId))).ToList());
    }

    private static IncomeDividendSumDto DividendSum(IncomeDividendSum sum) =>
        new(sum.Count, sum.Gross, sum.ImputationCredit, sum.GrossWithImputation, sum.ForeignTax, sum.Rwt, sum.Nrwt);

    /// <summary>Tax-cost-threshold R24: the threshold test with PublicIds.</summary>
    private static ThresholdDto Threshold(ThresholdTest test, CustomerTaxLedgerRead read)
    {
        Guid InstrumentId(int id) => read.Instruments[id].PublicId;
        var peak = test.Peak;
        return new ThresholdDto(
            test.Amount,
            test.Opening,
            new ThresholdPeakDto(
                peak.Total, peak.At, peak.InstrumentId is { } id ? InstrumentId(id) : null, peak.Event?.TransactionPublicId),
            test.Closing,
            test.Outcome.ToString(),
            test.Complete,
            test.Incomplete
                .Select(reason => new ThresholdIncompleteDto(
                    reason.Reason.ToString(), reason.InstrumentId is { } instrumentId ? InstrumentId(instrumentId) : null))
                .ToList(),
            test.Steps
                .Select(step => new ThresholdStepDto(
                    step.Event.BookedAt,
                    InstrumentId(step.Event.InstrumentId),
                    step.EventSequence,
                    step.Event.TransactionPublicId,
                    step.Event.Kind.ToString(),
                    step.Change,
                    step.Total))
                .ToList());
    }

    /// <summary>FIF income R26: the totals with the PublicId of each Instrument not computed.</summary>
    private static FifIncomeDto Income(FifIncome income, CustomerTaxLedgerRead read) =>
        new(
            income.Rate,
            income.Fdr is { } fdr
                ? new FifFdrTotalsDto(fdr.OpeningValue, fdr.Amount, fdr.PeakHoldingAmount, fdr.QuickSaleGainAmount, fdr.QuickSaleAdjustment, fdr.Income)
                : null,
            income.Cv is { } cv ? new FifCvTotalsDto(cv.Amount, cv.Income) : null,
            income.Lower?.ToString(),
            income.Complete,
            income.Incomplete
                .Select(item => new FifIncomeIncompleteDto(item.Reason.ToString(), read.Instruments[item.InstrumentId].PublicId))
                .ToList());

    /// <summary>FIF income R26: the income of one tax holding.</summary>
    private static TaxHoldingIncomeDto Income(TaxHoldingIncome income)
    {
        var sale = income.QuickSale;
        return new TaxHoldingIncomeDto(
            income.Status.ToString(),
            income.Reason?.ToString(),
            income.Fdr is { } fdr ? new FifFdrDto(fdr.OpeningValue, fdr.Amount) : null,
            sale is null
                ? null
                : new QuickSaleDto(
                    sale.OpeningQuantity,
                    sale.PeakQuantity,
                    sale.ClosingQuantity,
                    sale.PeakHoldingDifferential,
                    sale.PurchasedQuantity,
                    sale.PurchaseCost,
                    sale.AverageCost,
                    sale.PeakHoldingAmount,
                    sale.MatchedQuantity,
                    sale.Proceeds,
                    sale.Dividends,
                    sale.Cost,
                    sale.GainAmount,
                    sale.Matches.Select(match => new QuickSaleMatchDto(match.PurchaseSequence, match.SaleSequence, match.Quantity)).ToList(),
                    sale.Sales.Select(s => new QuickSaleSaleDto(s.Sequence, s.Quantity, s.MatchedQuantity, s.Amount, s.Proceeds)).ToList(),
                    sale.DividendShares
                        .Select(share => new QuickSaleDividendShareDto(share.Sequence, share.Amount, share.HeldQuantity, share.MatchedQuantity, share.Share))
                        .ToList()),
            income.Cv is { } cv ? new FifCvDto(cv.OpeningValue, cv.ClosingValue, cv.Sales, cv.Dividends, cv.Purchases, cv.Amount) : null);
    }

    private static TaxCostDto Cost(TaxCost cost) =>
        new(
            cost.Status.ToString(),
            cost.Opening,
            cost.Closing,
            cost.Unknown is { } unknown ? new TaxCostUnknownDto(unknown.Cause.ToString(), unknown.At, unknown.TransactionPublicId) : null);

    private static TaxHoldingDto Holding(TaxHolding holding, CustomerTaxLedgerRead read)
    {
        var instrument = read.Instruments[holding.InstrumentId];
        var treatment = holding.Treatment;
        return new TaxHoldingDto(
            new TaxInstrumentDto(instrument.PublicId, instrument.Symbol, instrument.Name, instrument.QuoteCurrency),
            new TaxTreatmentDto(treatment.FifTreatment.ToString(), treatment.RowTaxYear, treatment.Note, treatment.IssuerCountry),
            Point(holding.Opening, read),
            Point(holding.Closing, read),
            holding.PeakQuantity,
            holding.Events.Select(e => Event(e, read)).ToList(),
            holding.Complete,
            holding.Incomplete.Select(reason => reason.ToString()).ToList(),
            holding.Notices.Select(notice => notice.ToString()).ToList(),
            Cost(holding.Cost),
            Income(holding.Income!));
    }

    private static TaxPointDto Point(TaxPoint point, CustomerTaxLedgerRead read) =>
        new(
            point.At,
            point.ValuationDay,
            point.Quantity,
            point.ComparableQuantity,
            point.Value,
            point.Status.ToString(),
            point.Lines.Select(line => new TaxPointLineDto(
                    read.AccountIds[line.AccountId],
                    line.CustodyMode.ToString(),
                    line.Line.Quantity,
                    line.Line.LedgerQuantity,
                    line.Line.Status.ToString(),
                    Price(line.Line.Price),
                    line.Line.MarketValue,
                    Fx(line.Line.Fx),
                    line.Line.Value))
                .ToList());

    private static TaxEventDto Event(TaxYearEvent yearEvent, CustomerTaxLedgerRead read)
    {
        var taxEvent = yearEvent.Event;
        object? ledger = taxEvent switch
        {
            { Trade: { } trade } => new TaxTradeLedgerDto(trade.Currency, trade.Principal, trade.Fee),
            { Dividend: { } dividend } => new TaxDividendLedgerDto(
                dividend.Currency, dividend.Gross, dividend.ForeignTax, dividend.Rwt, dividend.Nrwt, dividend.ImputationCredit),
            _ => null
        };
        var dividendAmounts = yearEvent.Dividend is { } amounts
            ? new TaxDividendDto(
                amounts.Shares, amounts.TaxSource.ToString(), amounts.Gross, amounts.ForeignTax, amounts.Rwt, amounts.Nrwt, amounts.ImputationCredit)
            : null;
        var move = taxEvent.Move is { } legs
            ? new TaxMoveDto(
                legs.Quantity,
                read.AccountIds[legs.From.AccountId],
                legs.From.TransactionPublicId,
                legs.From.BookedAt,
                read.AccountIds[legs.To.AccountId],
                legs.To.TransactionPublicId,
                legs.To.BookedAt)
            : null;
        return new TaxEventDto(
            yearEvent.Sequence,
            taxEvent.Kind.ToString(),
            taxEvent.BookedAt,
            taxEvent.TransactionPublicId,
            read.AccountIds[taxEvent.AccountId],
            taxEvent.CustodyMode.ToString(),
            taxEvent.Quantity,
            yearEvent.QuantityAfter,
            yearEvent.ComparableQuantityAfter,
            yearEvent.Amount,
            yearEvent.Status.ToString(),
            ledger,
            Fx(yearEvent.Fx),
            Price(yearEvent.Price),
            yearEvent.Principal,
            yearEvent.Fee,
            dividendAmounts,
            taxEvent.CarriedCost is { } cost ? new TaxCarriedCostDto(cost.Amount, cost.Currency, cost.AcquiredOn) : null,
            move,
            yearEvent.Cost is { } eventCost ? new TaxEventCostDto(eventCost.Change, eventCost.After, Fx(eventCost.Fx)) : null);
    }

    private static AccountValuationPriceDto? Price(StoredPrice? price) =>
        price is null ? null : new AccountValuationPriceDto(price.Amount, price.Currency, price.AsOfDate, price.Source);

    private static AccountValuationFxDto? Fx(StoredRate? rate) =>
        rate is null ? null : new AccountValuationFxDto(rate.Rate, rate.AsOfDate, rate.Source);
}
