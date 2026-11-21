using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWealthV2.Application.AccountClosures;
using MyWealthV2.Application.AccountClosures.Commands;
using MyWealthV2.Application.AccountClosures.Queries;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Web.Endpoints;

/// <summary>The account closures of an Account, a sub-resource of <c>/accounts</c>
/// (docs/features/account-closure.md §4, §8).</summary>
public class AccountClosures : IEndpointGroup
{
    public static string RoutePrefix => "/accounts";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetAccountClosures, "{accountId}/closures").RequireAuthorization(Policies.AccountClosuresRead);
        groupBuilder.MapGet(GetAccountClosure, "{accountId}/closures/{id}").RequireAuthorization(Policies.AccountClosuresRead);
        groupBuilder.MapPost(RequestAccountClosure, "{accountId}/closures").RequireAuthorization(Policies.AccountClosuresRequest);
    }

    [EndpointSummary("List the closures of an account")]
    [EndpointDescription("Returns the account's closures, newest requested first (R26).")]
    public static async Task<Ok<PagedList<AccountClosureDto>>> GetAccountClosures(
        ISender sender,
        Guid accountId,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        Guid? tenantId = null) =>
        TypedResults.Ok(await sender.Send(new GetAccountClosuresQuery(accountId, page, pageSize, status, tenantId)));

    [EndpointSummary("Get an account closure")]
    [EndpointDescription("Returns one closure of the account (R25).")]
    public static async Task<Ok<AccountClosureDto>> GetAccountClosure(ISender sender, Guid accountId, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetAccountClosureByIdQuery(accountId, id)));

    [EndpointSummary("Request an account closure")]
    [EndpointDescription("Closes a Custodial account that holds nothing and has nothing in progress: pays out its cash as a withdrawal, writes off the remainder below the minor unit and closes the account when the bank has paid, or at once when there is nothing to pay out. Requires Idempotency-Key (R2–R10).")]
    public static async Task<IResult> RequestAccountClosure(
        ISender sender,
        HttpRequest httpRequest,
        Guid accountId,
        RequestAccountClosureCommand command)
    {
        var key = httpRequest.Headers["Idempotency-Key"].ToString();
        command.PathAccountId = accountId;
        command.IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key;
        command.RequestMethod = httpRequest.Method;
        command.RequestPath = httpRequest.Path.Value ?? $"{RoutePrefix}/{accountId}/closures";
        var result = await sender.Send(command);
        if (result.StatusCode == StatusCodes.Status201Created)
        {
            using var body = JsonDocument.Parse(result.Body);
            httpRequest.HttpContext.Response.Headers.Location =
                $"{RoutePrefix}/{accountId}/closures/{body.RootElement.GetProperty("id").GetGuid()}";
        }

        return Results.Content(result.Body, "application/json", statusCode: result.StatusCode);
    }
}
