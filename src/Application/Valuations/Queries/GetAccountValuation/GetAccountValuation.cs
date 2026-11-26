using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Valuations.Queries.GetAccountValuation;

/// <summary>Account valuation R1, R20–R22: what one Account was worth at the end of one UTC day.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetAccountValuationQuery(Guid AccountId, string? Date) : IRequest<AccountValuationDto>;

/// <summary>R20: <c>date</c> is required, a <c>yyyy-MM-dd</c> date, and not after the platform's UTC date.</summary>
public class GetAccountValuationQueryValidator : AbstractValidator<GetAccountValuationQuery>
{
    public GetAccountValuationQueryValidator(TimeProvider time)
    {
        RuleFor(query => query.Date)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Date is required.")
            .Must(MarketDataDates.IsEmptyOrDate).WithMessage($"Date {MarketDataDates.FormatMessage}")
            .Must(date => MarketDataDates.Parse(date) <= LedgerDay.Of(time.GetUtcNow()))
            .WithMessage("A valuation can be read only for today or a past day.");
    }
}

public class GetAccountValuationQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, IValueAccount valueAccount)
    : IRequestHandler<GetAccountValuationQuery, AccountValuationDto>
{
    public async Task<AccountValuationDto> Handle(GetAccountValuationQuery request, CancellationToken cancellationToken)
    {
        // R1: found as GET /accounts/{id}/holdings finds it; any other Account is 404.
        var accountRow = await ValuationAccounts.FindAsync(db, currentUser, request.AccountId, cancellationToken);

        var day = MarketDataDates.Parse(request.Date)!.Value;
        var read = await valueAccount.ForDayAsync(accountRow, day, cancellationToken);
        var valuation = read.Valuation;

        // R21: holdings sorted by symbol, then instrument id.
        var holdings = valuation.Lines
            .Select(line => (Line: line, Instrument: read.Instruments[line.InstrumentId]))
            .OrderBy(row => row.Instrument.Symbol, StringComparer.Ordinal)
            .ThenBy(row => row.Instrument.PublicId)
            .Select(row => new AccountValuationHoldingDto(
                row.Instrument.PublicId,
                row.Instrument.Symbol,
                row.Instrument.Name,
                row.Line.Quantity,
                row.Line.LedgerQuantity,
                row.Line.Status.ToString(),
                row.Line.Price is { } price
                    ? new AccountValuationPriceDto(price.Amount, price.Currency, price.AsOfDate, price.Source)
                    : null,
                row.Line.MarketValue,
                row.Line.Fx is { } fx ? new AccountValuationFxDto(fx.Rate, fx.AsOfDate, fx.Source) : null,
                row.Line.Value))
            .ToList();

        return new AccountValuationDto(
            accountRow.PublicId,
            valuation.Day,
            valuation.Currency,
            valuation.Cash,
            holdings,
            valuation.HoldingsValue,
            valuation.Total,
            valuation.Complete,
            valuation.UnvaluedCount);
    }
}
