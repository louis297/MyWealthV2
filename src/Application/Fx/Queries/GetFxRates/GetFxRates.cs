using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.Fx.Queries.GetFxRates;

/// <summary>
/// Market data history R20–R22: the stored rates of one directed pair, newest first. Platform reference data: any
/// authenticated caller, as <c>GET /currencies</c>. <c>From</c> and <c>To</c> are <c>yyyy-MM-dd</c>, both inclusive.
/// </summary>
public record GetFxRatesQuery(
    string? Base,
    string? Quote,
    string? From = null,
    string? To = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedList<FxRateDto>>;

/// <summary>One stored rate: <c>rate</c> units of <c>quote</c> per 1 <c>base</c>.</summary>
public sealed record FxRateDto(DateOnly Date, string Base, string Quote, decimal Rate, string Source, DateTimeOffset FetchedAt);

public class GetFxRatesQueryHandler(IFxRate rates) : IRequestHandler<GetFxRatesQuery, PagedList<FxRateDto>>
{
    public Task<PagedList<FxRateDto>> Handle(GetFxRatesQuery request, CancellationToken cancellationToken)
    {
        _ = rates;
        return Task.FromResult(new PagedList<FxRateDto> { Items = [], Page = request.Page, PageSize = request.PageSize, TotalCount = 0 });
    }
}
