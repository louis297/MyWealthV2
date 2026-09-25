using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Application.Transactions.Commands.CreateTransaction;
using MyWealthV2.Application.Transactions.Queries.GetTransactionById;
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
    public static Task<IResult> GetTransactions() =>
        throw new NotImplementedException();

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
