using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Fx.Queries.GetFxRates;

namespace MyWealthV2.Web.Endpoints;

public class FxRates : IEndpointGroup
{
    public static string RoutePrefix => "/fx-rates";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetFxRates).RequireAuthorization();
    }

    [EndpointSummary("List the stored rates of a currency pair")]
    [EndpointDescription("The stored FX rates of one directed pair (quote per 1 base), newest first. Optional from and to (yyyy-MM-dd, inclusive); to with pageSize=1 answers the rate a read as of that date chooses.")]
    public static async Task<Ok<PagedList<FxRateDto>>> GetFxRates(
        ISender sender,
        [FromQuery(Name = "base")] string? baseCurrency = null,
        [FromQuery(Name = "quote")] string? quoteCurrency = null,
        string? from = null,
        string? to = null,
        int page = 1,
        int pageSize = 20) =>
        TypedResults.Ok(await sender.Send(new GetFxRatesQuery(baseCurrency, quoteCurrency, from, to, page, pageSize)));
}
