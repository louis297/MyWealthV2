using System.Globalization;

namespace MyWealthV2.Application.Countries.Queries.GetCountries;

/// <summary>Instrument-tax-attributes R6: <c>GET /countries</c>, for any authenticated caller.</summary>
public record GetCountriesQuery : IRequest<IReadOnlyList<CountryDto>>;

/// <summary>One country of the list: its ISO 3166-1 alpha-2 code and English short name.</summary>
public sealed record CountryDto(string Code, string Name);

public class GetCountriesQueryHandler : IRequestHandler<GetCountriesQuery, IReadOnlyList<CountryDto>>
{
    /// <summary>The constant list of <see cref="Domain.Instruments.Countries"/>, sorted by name once.</summary>
    private static readonly IReadOnlyList<CountryDto> Sorted = Domain.Instruments.Countries.All
        .OrderBy(country => country.Name, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: false))
        .Select(country => new CountryDto(country.Code, country.Name))
        .ToList();

    public Task<IReadOnlyList<CountryDto>> Handle(GetCountriesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Sorted);
}
