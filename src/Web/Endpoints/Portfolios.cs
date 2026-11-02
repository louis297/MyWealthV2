using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Application.Portfolios.Commands.CreatePortfolio;
using MyWealthV2.Application.Portfolios.Commands.RebalancePortfolio;
using MyWealthV2.Application.Portfolios.Commands.DisablePortfolio;
using MyWealthV2.Application.Portfolios.Commands.UpdatePortfolio;
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
        groupBuilder.MapPut(UpdatePortfolio, "{id}").RequireAuthorization(Policies.PortfoliosManage);
        groupBuilder.MapPost(DisablePortfolio, "{id}/disable").RequireAuthorization(Policies.PortfoliosManage);
        groupBuilder.MapPost(RebalancePortfolio, "{id}/rebalance").RequireAuthorization(Policies.TransactionsCreate);
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

    [EndpointSummary("Update a portfolio")]
    [EndpointDescription("Renames a portfolio and replaces its lines. Base currency cannot change.")]
    public static async Task<Results<NoContent, Accepted<RebalanceAcceptedDto>>> UpdatePortfolio(
        ISender sender,
        Guid id,
        UpdatePortfolioCommand command)
    {
        command.Id = id;
        var accepted = await sender.Send(command);
        return accepted is null
            ? TypedResults.NoContent()
            : TypedResults.Accepted($"/portfolios/{id}/rebalance", accepted);
    }

    [EndpointSummary("Disable a portfolio")]
    [EndpointDescription("Disables a portfolio and removes every active assignment. Holdings are left untouched.")]
    public static async Task<NoContent> DisablePortfolio(ISender sender, Guid id, DisablePortfolioCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Rebalance a portfolio")]
    [EndpointDescription("Enqueues a full rebalance for every active assignment.")]
    public static async Task<Accepted<RebalanceAcceptedDto>> RebalancePortfolio(ISender sender, Guid id)
    {
        var accepted = await sender.Send(new RebalancePortfolioCommand { Id = id });
        return TypedResults.Accepted($"/portfolios/{id}/rebalance", accepted);
    }
}

public sealed record CreatedPortfolioDto(Guid Id);
