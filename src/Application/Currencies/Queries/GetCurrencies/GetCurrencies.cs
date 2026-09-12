using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Currencies.Queries.GetCurrencies;

public record GetCurrenciesQuery(string? EnabledOnly = null) : IRequest<IReadOnlyList<CurrencyDto>>;

public class GetCurrenciesQueryValidator : AbstractValidator<GetCurrenciesQuery>
{
    public GetCurrenciesQueryValidator()
    {
        RuleFor(query => query.EnabledOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("enabledOnly must be true or false.");
    }
}

public class GetCurrenciesQueryHandler(ICurrencyCatalog catalog)
    : IRequestHandler<GetCurrenciesQuery, IReadOnlyList<CurrencyDto>>
{
    public Task<IReadOnlyList<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
    {
        var enabledOnly = bool.TryParse(request.EnabledOnly, out var flag) && flag;
        IReadOnlyList<CurrencyDto> items = catalog.List(enabledOnly).Select(CurrencyDto.From).ToList();
        return Task.FromResult(items);
    }
}
