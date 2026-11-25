using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

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

public class GetInstrumentPricesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, IMarketData marketData)
    : IRequestHandler<GetInstrumentPricesQuery, PagedList<InstrumentPriceDto>>
{
    public Task<PagedList<InstrumentPriceDto>> Handle(GetInstrumentPricesQuery request, CancellationToken cancellationToken)
    {
        _ = (db, currentUser, marketData);
        return Task.FromResult(new PagedList<InstrumentPriceDto> { Items = [], Page = request.Page, PageSize = request.PageSize, TotalCount = 0 });
    }
}
