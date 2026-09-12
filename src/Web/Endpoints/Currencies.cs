using MyWealthV2.Application.Currencies;
using MyWealthV2.Application.Currencies.Queries.GetCurrencies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Currencies : IEndpointGroup
{
    public static string RoutePrefix => "/currencies";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetCurrencies).RequireAuthorization();
    }

    [EndpointSummary("List platform currencies")]
    [EndpointDescription("Returns the platform currency catalog. Omit enabledOnly or set false for all rows; true returns enabled rows only.")]
    public static async Task<Ok<IReadOnlyList<CurrencyDto>>> GetCurrencies(ISender sender, string? enabledOnly)
    {
        var items = await sender.Send(new GetCurrenciesQuery(enabledOnly));
        return TypedResults.Ok(items);
    }
}
