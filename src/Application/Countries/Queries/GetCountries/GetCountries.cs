namespace MyWealthV2.Application.Countries.Queries.GetCountries;

/// <summary>Instrument-tax-attributes R6: <c>GET /countries</c>, for any authenticated caller.</summary>
public record GetCountriesQuery : IRequest<IReadOnlyList<CountryDto>>;

/// <summary>One country of the list: its ISO 3166-1 alpha-2 code and English short name.</summary>
public sealed record CountryDto(string Code, string Name);

public class GetCountriesQueryHandler : IRequestHandler<GetCountriesQuery, IReadOnlyList<CountryDto>>
{
    public Task<IReadOnlyList<CountryDto>> Handle(GetCountriesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CountryDto>>([]);
}
