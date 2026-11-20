using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.StatementLines;
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
