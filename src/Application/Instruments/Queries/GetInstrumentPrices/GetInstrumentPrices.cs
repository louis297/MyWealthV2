using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Instruments.Queries.GetInstrumentPrices;

/// <summary>
/// Market data history R19, R21–R23: the stored prices of one instrument the caller may read, newest first.
/// <c>From</c> and <c>To</c> are <c>yyyy-MM-dd</c>, both inclusive.
/// </summary>
[Authorize(Policy = Policies.InstrumentsRead)]
public record GetInstrumentPricesQuery(
    Guid Id,
    string? From = null,
    string? To = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedList<InstrumentPriceDto>>;

/// <summary>One stored price: <c>currency</c> is the instrument's quote currency.</summary>
public sealed record InstrumentPriceDto(DateOnly Date, decimal Price, string Currency, string Source, DateTimeOffset FetchedAt);

public class GetInstrumentPricesQueryValidator : AbstractValidator<GetInstrumentPricesQuery>
{
    public GetInstrumentPricesQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.From).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"From {MarketDataDates.FormatMessage}");
        RuleFor(query => query.To).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"To {MarketDataDates.FormatMessage}");
        RuleFor(query => query.From)
            .Must((query, from) => !MarketDataDates.FromIsAfterTo(from, query.To))
            .WithMessage("From must not be after To.");
    }
}

public class GetInstrumentPricesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, IMarketData marketData)
    : IRequestHandler<GetInstrumentPricesQuery, PagedList<InstrumentPriceDto>>
{
    public async Task<PagedList<InstrumentPriceDto>> Handle(GetInstrumentPricesQuery request, CancellationToken cancellationToken)
    {
        // R19: found as GET /instruments/{id} finds it; an inactive instrument is readable.
        var instrument = await InstrumentVisibility.Readable(db, currentUser)
                             .Where(row => row.PublicId == request.Id)
                             .Select(row => new { row.Id, row.QuoteCurrency })
                             .SingleOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("Instrument", request.Id);

        var rows = await marketData.ListPricesAsync(
            instrument.Id,
            MarketDataDates.Parse(request.From),
            MarketDataDates.Parse(request.To),
            request.Page,
            request.PageSize,
            cancellationToken);
        return new PagedList<InstrumentPriceDto>
        {
            Items = rows.Items
                .Select(row => new InstrumentPriceDto(row.AsOfDate, row.Price, instrument.QuoteCurrency, row.Source, row.FetchedAt))
                .ToList(),
            Page = rows.Page,
            PageSize = rows.PageSize,
            TotalCount = rows.TotalCount
        };
    }
}
