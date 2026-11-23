using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MigrationBatches;
using MyWealthV2.Application.MigrationBatches.Commands;
using MyWealthV2.Application.MigrationBatches.Queries;

namespace MyWealthV2.Web.Endpoints;

/// <summary>The migration batches of the caller's Tenant (docs/features/migration-batches.md §8).</summary>
public class MigrationBatches : IEndpointGroup
{
    public static string RoutePrefix => "/migration-batches";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetMigrationBatches).RequireAuthorization(Policies.MigrationBatchesManage);
        groupBuilder.MapGet(GetMigrationBatch, "{id:guid}").RequireAuthorization(Policies.MigrationBatchesManage);
        groupBuilder.MapPost(CreateMigrationBatch).RequireAuthorization(Policies.MigrationBatchesManage);
        groupBuilder.MapPut(ReplaceMigrationBatch, "{id:guid}").RequireAuthorization(Policies.MigrationBatchesManage);
        groupBuilder.MapPost(SubmitMigrationBatch, "{id:guid}/submit").RequireAuthorization(Policies.MigrationBatchesManage);
        groupBuilder.MapPost(ReturnMigrationBatch, "{id:guid}/return").RequireAuthorization(Policies.MigrationBatchesManage);
        groupBuilder.MapPost(CancelMigrationBatch, "{id:guid}/cancel").RequireAuthorization(Policies.MigrationBatchesManage);
        groupBuilder.MapPost(PostMigrationBatch, "{id:guid}/post").RequireAuthorization(Policies.MigrationBatchesManage);
    }

    [EndpointSummary("List migration batches")]
    [EndpointDescription("Returns the migration batches of the caller's Tenant, newest created first. Filter: status (comma-separated) (R28).")]
    public static async Task<Ok<PagedList<MigrationBatchDto>>> GetMigrationBatches(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetMigrationBatchesQuery(page, pageSize, status, tenantId)));

    [EndpointSummary("Get a migration batch")]
    [EndpointDescription("Returns one migration batch with its control, computed and funding totals, its funding lines, its entries and, while it is a Draft or Submitted, its validation (R29).")]
    public static async Task<Ok<MigrationBatchItemDto>> GetMigrationBatch(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetMigrationBatchByIdQuery(id)));

    [EndpointSummary("Create a migration batch")]
    [EndpointDescription("Creates a Draft migration batch with its header, control totals and entries; the entries may be empty (R6, R7).")]
    public static async Task<Created<CreatedIdDto>> CreateMigrationBatch(ISender sender, CreateMigrationBatchCommand command)
    {
        var created = await sender.Send(command);
        return TypedResults.Created($"{RoutePrefix}/{created.Id}", created);
    }

    [EndpointSummary("Replace a migration batch")]
    [EndpointDescription("Replaces the header, the control totals and every entry of a Draft migration batch. Its funding lines stay (R6, R7).")]
    public static async Task<NoContent> ReplaceMigrationBatch(ISender sender, Guid id, ReplaceMigrationBatchCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Submit a migration batch")]
    [EndpointDescription("Submits a valid Draft migration batch for a second TenantAdmin to post. A batch with an issue is refused with migration_batch_not_valid (R17).")]
    public static async Task<NoContent> SubmitMigrationBatch(ISender sender, Guid id, SubmitMigrationBatchCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Return a migration batch")]
    [EndpointDescription("Sends a Submitted migration batch back to Draft with a required note (R18).")]
    public static async Task<NoContent> ReturnMigrationBatch(ISender sender, Guid id, ReturnMigrationBatchCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Cancel a migration batch")]
    [EndpointDescription("Ends a Draft or Submitted migration batch with a required note. Its funding lines go back to suspense (R19).")]
    public static async Task<NoContent> CancelMigrationBatch(ISender sender, Guid id, CancelMigrationBatchCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Post a migration batch")]
    [EndpointDescription("Posts a Submitted migration batch: one Opening and one OpeningHoldings per entry with source Migration, all of them or none. The caller must not be the person who submitted it (R20–R23).")]
    public static async Task<NoContent> PostMigrationBatch(ISender sender, Guid id, PostMigrationBatchCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
}
