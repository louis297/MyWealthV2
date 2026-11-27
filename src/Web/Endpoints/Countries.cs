using MyWealthV2.Application.Countries.Queries.GetCountries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Countries : IEndpointGroup
{
    public static string RoutePrefix => "/countries";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetCountries).RequireAuthorization();
    }

    [EndpointSummary("List countries")]
    [EndpointDescription("Returns every officially assigned ISO 3166-1 alpha-2 code with its English short name, sorted by name. The list is a constant; it has no paging.")]
    public static async Task<Ok<IReadOnlyList<CountryDto>>> GetCountries(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetCountriesQuery()));
}
