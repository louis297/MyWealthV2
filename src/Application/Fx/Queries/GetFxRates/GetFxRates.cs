using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.MarketData;

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

public class GetFxRatesQueryValidator : AbstractValidator<GetFxRatesQuery>
{
    public GetFxRatesQueryValidator(ICurrencyCatalog catalog)
    {
        // R20: three letters, a code of the catalog (active or not), compared without regard to case.
        bool InCatalog(string? code) =>
            code is { Length: 3 } && code.All(char.IsAsciiLetter) && catalog.TryGet(code.ToUpperInvariant()) is not null;

        RuleFor(query => query.Base).Must(InCatalog).WithMessage("Base must be the three-letter code of a catalog currency.");
        RuleFor(query => query.Quote).Must(InCatalog).WithMessage("Quote must be the three-letter code of a catalog currency.");
        RuleFor(query => query.Quote)
            .Must((query, quote) => !string.Equals(query.Base, quote, StringComparison.OrdinalIgnoreCase))
            .When(query => InCatalog(query.Quote))
            .WithMessage("No rate is stored for a currency against itself.");
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.From).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"From {MarketDataDates.FormatMessage}");
        RuleFor(query => query.To).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"To {MarketDataDates.FormatMessage}");
        RuleFor(query => query.From)
            .Must((query, from) => !MarketDataDates.FromIsAfterTo(from, query.To))
            .WithMessage("From must not be after To.");
    }
}

public class GetFxRatesQueryHandler(IFxRate rates) : IRequestHandler<GetFxRatesQuery, PagedList<FxRateDto>>
{
    public async Task<PagedList<FxRateDto>> Handle(GetFxRatesQuery request, CancellationToken cancellationToken)
    {
        var rows = await rates.ListRatesAsync(
            request.Base!,
            request.Quote!,
            MarketDataDates.Parse(request.From),
            MarketDataDates.Parse(request.To),
            request.Page,
            request.PageSize,
            cancellationToken);
        return new PagedList<FxRateDto>
        {
            Items = rows.Items
                .Select(row => new FxRateDto(row.AsOfDate, row.BaseCurrency, row.QuoteCurrency, row.Rate, row.Source, row.FetchedAt))
                .ToList(),
            Page = rows.Page,
            PageSize = rows.PageSize,
            TotalCount = rows.TotalCount
        };
    }
}
