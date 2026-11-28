using System.Globalization;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.TaxYears.Queries.GetCustomerTaxYear;

/// <summary>Tax-year-events R1, R4, R30–R32: one Customer's tax events and holdings of one tax year, in NZD.</summary>
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
            null!);
    }

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
            null!);
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
