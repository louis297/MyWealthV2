using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.StatementLines;
using MyWealthV2.Application.StatementLines.Commands;
using MyWealthV2.Application.StatementLines.Queries.GetStatementLineById;
using MyWealthV2.Application.StatementLines.Queries.GetStatementLines;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

/// <summary>Bank statement lines and suspense (docs/features/deposit-matching.md §4.4, §8). TenantAdmin only.</summary>
public class StatementLines : IEndpointGroup
{
    public static string RoutePrefix => "/statement-lines";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetStatementLines).RequireAuthorization(Policies.StatementLinesManage);
        groupBuilder.MapGet(GetStatementLine, "{id}").RequireAuthorization(Policies.StatementLinesManage);
        groupBuilder.MapPost(MatchStatementLine, "{id}/match").RequireAuthorization(Policies.StatementLinesManage);
        groupBuilder.MapPost(IgnoreStatementLine, "{id}/ignore").RequireAuthorization(Policies.StatementLinesManage);
        groupBuilder.MapPost(RequestStatementLineReturn, "{id}/request-return").RequireAuthorization(Policies.StatementLinesManage);
        groupBuilder.MapPost(ReopenStatementLine, "{id}/reopen").RequireAuthorization(Policies.StatementLinesManage);
    }

    [EndpointSummary("Match a statement line")]
    [EndpointDescription("Matches an unmatched receipt to a Custodial account by hand and posts the deposit (R26).")]
    public static async Task<NoContent> MatchStatementLine(ISender sender, Guid id, MatchStatementLineCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Ignore a statement line")]
    [EndpointDescription("Marks an unmatched receipt as not Customer money, with a note (R27).")]
    public static async Task<NoContent> IgnoreStatementLine(ISender sender, Guid id, IgnoreStatementLineCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Request the return of a statement line")]
    [EndpointDescription("Marks an unmatched receipt to be returned to the payer, with a note (R28).")]
    public static async Task<NoContent> RequestStatementLineReturn(
        ISender sender,
        Guid id,
        RequestStatementLineReturnCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Reopen a statement line")]
    [EndpointDescription("Puts an ignored or to-be-returned receipt back into suspense (R29).")]
    public static async Task<NoContent> ReopenStatementLine(ISender sender, Guid id, ReopenStatementLineCommand command)
    {
        command.Id = id;
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("List statement lines")]
    [EndpointDescription("Returns the current tenant's bank statement lines, newest first. TenantAdmin only.")]
    public static async Task<Ok<PagedList<StatementLineDto>>> GetStatementLines(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        string? currency = null,
        Guid? clientMoneyAccountId = null,
        Guid? tenantId = null)
    {
        var result = await sender.Send(
            new GetStatementLinesQuery(page, pageSize, status, currency, clientMoneyAccountId, tenantId));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a statement line")]
    [EndpointDescription("Returns one bank statement line of the current tenant. TenantAdmin only.")]
    public static async Task<Ok<StatementLineDto>> GetStatementLine(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetStatementLineByIdQuery(id));
        return TypedResults.Ok(item);
    }
}
