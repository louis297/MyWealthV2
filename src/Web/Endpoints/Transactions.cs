using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Application.Transactions.Commands.CreateTransaction;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Transactions.Queries.GetTransactionById;
using MyWealthV2.Application.Transactions.Queries.GetTransactions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWealthV2.Web.Endpoints;

public class Transactions : IEndpointGroup
{
    public static string RoutePrefix => "/transactions";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetTransactions).RequireAuthorization(Policies.TransactionsRead);
        groupBuilder.MapGet(GetTransaction, "{id}").RequireAuthorization(Policies.TransactionsRead);
        groupBuilder.MapPost(CreateTransaction).RequireAuthorization(Policies.TransactionsCreate);
    }

    [EndpointSummary("List transactions")]
    [EndpointDescription("Returns a paged list of transactions in one tenant.")]
    public static async Task<Ok<PagedList<TransactionDto>>> GetTransactions(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? tenantId = null,
        Guid? accountId = null,
        Guid? customerId = null)
    {
        var result = await sender.Send(new GetTransactionsQuery(page, pageSize, tenantId, accountId, customerId));
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get a transaction")]
    [EndpointDescription("Returns one transaction by PublicId.")]
    public static async Task<Ok<TransactionDto>> GetTransaction(ISender sender, Guid id)
    {
        var item = await sender.Send(new GetTransactionByIdQuery(id));
        return TypedResults.Ok(item);
    }

    [EndpointSummary("Create a transaction")]
    [EndpointDescription("Posts a cash transaction on an open account. Requires Idempotency-Key.")]
    public static async Task<IResult> CreateTransaction(
        ISender sender,
        HttpRequest httpRequest,
        CreateTransactionCommand command)
    {
        var key = httpRequest.Headers["Idempotency-Key"].ToString();
        command.IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key;
        command.RequestMethod = httpRequest.Method;
        command.RequestPath = httpRequest.Path.Value ?? "/transactions";
        var result = await sender.Send(command);
        return Results.Content(result.Body, "application/json", statusCode: result.StatusCode);
    }
}
