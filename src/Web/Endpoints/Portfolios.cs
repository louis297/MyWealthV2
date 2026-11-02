using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Application.Portfolios.Commands.CreatePortfolio;
using MyWealthV2.Application.Portfolios.Queries.GetPortfolioById;
using MyWealthV2.Application.Portfolios.Queries.GetPortfolios;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Portfolios : IEndpointGroup
{
    public static string RoutePrefix => "/portfolios";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetPortfolios).RequireAuthorization(Policies.PortfoliosRead);
        groupBuilder.MapGet(GetPortfolio, "{id}").RequireAuthorization(Policies.PortfoliosRead);
        groupBuilder.MapPost(CreatePortfolio).RequireAuthorization(Policies.PortfoliosManage);
    }

    [EndpointSummary("List portfolios")]
    [EndpointDescription("Returns a paged list of portfolios in one tenant.")]
    public static async Task<Ok<PagedList<PortfolioDto>>> GetPortfolios(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? tenantId = null)
    {
        var result = await sender.Send(new GetPortfoliosQuery(page, pageSize, tenantId));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a portfolio")]
    [EndpointDescription("Returns one portfolio by PublicId.")]
    public static async Task<Ok<PortfolioDto>> GetPortfolio(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetPortfolioByIdQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create a portfolio")]
    [EndpointDescription("Creates a model portfolio in the current tenant, or in a chosen tenant for SystemAdmin.")]
    public static async Task<Created<CreatedPortfolioDto>> CreatePortfolio(
        ISender sender,
        CreatePortfolioCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/portfolios/{id}", new CreatedPortfolioDto(id));
    }
}

public sealed record CreatedPortfolioDto(Guid Id);
