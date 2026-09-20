using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Instruments;
using MyWealthV2.Application.Instruments.Commands.CreateInstrument;
using MyWealthV2.Application.Instruments.Commands.DisableInstrument;
using MyWealthV2.Application.Instruments.Commands.EnableInstrument;
using MyWealthV2.Application.Instruments.Commands.UpdateInstrument;
using MyWealthV2.Application.Instruments.Queries.GetInstrumentById;
using MyWealthV2.Application.Instruments.Queries.GetInstruments;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Instruments : IEndpointGroup
{
    public static string RoutePrefix => "/instruments";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetInstruments).RequireAuthorization(Policies.InstrumentsRead);
        groupBuilder.MapGet(GetInstrument, "{id}").RequireAuthorization(Policies.InstrumentsRead);
        groupBuilder.MapPost(CreateInstrument).RequireAuthorization(Policies.InstrumentsCreate);
        groupBuilder.MapPut(UpdateInstrument, "{id}").RequireAuthorization(Policies.InstrumentsManage);
        groupBuilder.MapPost(DisableInstrument, "{id}/disable").RequireAuthorization(Policies.InstrumentsManage);
        groupBuilder.MapPost(EnableInstrument, "{id}/enable").RequireAuthorization(Policies.InstrumentsManage);
    }

    [EndpointSummary("List instruments")]
    [EndpointDescription("Returns a paged list of instruments in one tenant.")]
    public static async Task<Ok<PagedList<InstrumentDto>>> GetInstruments(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? enabledOnly = null,
        string? search = null,
        Guid? tenantId = null)
    {
        var result = await sender.Send(new GetInstrumentsQuery(page, pageSize, enabledOnly, search, tenantId));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get an instrument")]
    [EndpointDescription("Returns one instrument by PublicId.")]
    public static async Task<Ok<InstrumentDto>> GetInstrument(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetInstrumentByIdQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create an instrument")]
    [EndpointDescription("Creates an instrument in the current tenant, or in a chosen tenant for SystemAdmin.")]
    public static async Task<Created<CreatedInstrumentDto>> CreateInstrument(
        ISender sender,
        CreateInstrumentCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/instruments/{id}", new CreatedInstrumentDto(id));
    }

    [EndpointSummary("Update an instrument")]
    [EndpointDescription("Renames an instrument and/or changes its symbol. Quote currency cannot change.")]
    public static async Task<NoContent> UpdateInstrument(ISender sender, Guid id, UpdateInstrumentCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Disable an instrument")]
    [EndpointDescription("Disables an instrument. Idempotent.")]
    public static async Task<NoContent> DisableInstrument(ISender sender, Guid id, DisableInstrumentCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Enable an instrument")]
    [EndpointDescription("Re-enables an instrument. Idempotent.")]
    public static async Task<NoContent> EnableInstrument(ISender sender, Guid id, EnableInstrumentCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
