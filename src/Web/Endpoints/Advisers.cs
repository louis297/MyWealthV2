using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Users;
using MyWealthV2.Application.Users.Commands.CreateAdviser;
using MyWealthV2.Application.Users.Commands.DisableAdviser;
using MyWealthV2.Application.Users.Commands.EnableAdviser;
using MyWealthV2.Application.Users.Commands.UpdateAdviser;
using MyWealthV2.Application.Users.Queries.GetAdviserById;
using MyWealthV2.Application.Users.Queries.GetAdvisers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Advisers : IEndpointGroup
{
    public static string RoutePrefix => "/users/advisers";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetAdvisers).RequireAuthorization(Policies.AdvisersManage);
        groupBuilder.MapGet(GetAdviser, "{id}").RequireAuthorization(Policies.AdvisersManage);
        groupBuilder.MapPost(CreateAdviser).RequireAuthorization(Policies.AdvisersManage);
        groupBuilder.MapPut(UpdateAdviser, "{id}").RequireAuthorization(Policies.AdvisersManage);
        groupBuilder.MapPost(DisableAdviser, "{id}/disable").RequireAuthorization(Policies.AdvisersManage);
        groupBuilder.MapPost(EnableAdviser, "{id}/enable").RequireAuthorization(Policies.AdvisersManage);
    }

    [EndpointSummary("List advisers")]
    [EndpointDescription("Returns a paged list of Advisers in the current tenant. TenantAdmin only.")]
    public static async Task<Ok<PagedList<AdviserDto>>> GetAdvisers(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? enabledOnly = null,
        string? search = null)
    {
        var result = await sender.Send(new GetAdvisersQuery(page, pageSize, enabledOnly, search));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get an adviser")]
    [EndpointDescription("Returns one Adviser by PublicId in the current tenant. TenantAdmin only.")]
    public static async Task<Ok<AdviserDto>> GetAdviser(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetAdviserByIdQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create an adviser")]
    [EndpointDescription("Creates an Adviser with a password in the current tenant. TenantAdmin only.")]
    public static async Task<Created<CreatedAdviserDto>> CreateAdviser(
        ISender sender,
        CreateAdviserCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/users/advisers/{id}", new CreatedAdviserDto(id));
    }

    [EndpointSummary("Update an adviser")]
    [EndpointDescription("Renames an Adviser. Email, tenant, and role cannot change. TenantAdmin only.")]
    public static async Task<NoContent> UpdateAdviser(
        ISender sender,
        Guid id,
        UpdateAdviserCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Disable an adviser")]
    [EndpointDescription("Disables an Adviser. Idempotent. Assigned non-disabled customers must be handled first.")]
    public static async Task<NoContent> DisableAdviser(
        ISender sender,
        Guid id,
        DisableAdviserCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Enable an adviser")]
    [EndpointDescription("Re-enables an Adviser. Idempotent. TenantAdmin only.")]
    public static async Task<NoContent> EnableAdviser(
        ISender sender,
        Guid id,
        EnableAdviserCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
