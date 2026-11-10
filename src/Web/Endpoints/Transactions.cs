using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using System.Text.Json;
using MyWealthV2.Application.Transactions.Commands.CreateFxConversion;
using MyWealthV2.Application.Transactions.Commands.CreateInternalTransfer;
using MyWealthV2.Application.Transactions.Commands.CreateTransaction;
using MyWealthV2.Application.Transactions.Commands.ReverseTransaction;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Transactions.Queries.GetFxConversionQuote;
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
        groupBuilder.MapPost(CreateInternalTransfer, "transfer").RequireAuthorization(Policies.TransactionsCreate);
        groupBuilder.MapPost(CreateFxConversion, "fx").RequireAuthorization(Policies.TransactionsCreate);
        groupBuilder.MapPost(GetFxConversionQuote, "fx/quote").RequireAuthorization(Policies.TransactionsCreate);
        groupBuilder.MapPost(ReverseTransaction, "{id}/reverse").RequireAuthorization(Policies.TransactionsCreate);
    }

    [EndpointSummary("List transactions")]
    [EndpointDescription("Returns a paged list of transactions in one tenant.")]
    public static async Task<Ok<PagedList<TransactionDto>>> GetTransactions(
        ISender sender,
        int page = 1,
        int pageSize = 20,
        Guid? tenantId = null,
        Guid? accountId = null,
        Guid? customerId = null,
        string? type = null)
    {
        var result = await sender.Send(new GetTransactionsQuery(page, pageSize, tenantId, accountId, customerId, type));
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

    [EndpointSummary("Create an internal transfer")]
    [EndpointDescription("Moves cash from one Open account to another Open account with the same currency in the same tenant. Requires Idempotency-Key.")]
    public static async Task<IResult> CreateInternalTransfer(
        ISender sender,
        HttpRequest httpRequest,
        CreateInternalTransferCommand command)
    {
        var key = httpRequest.Headers["Idempotency-Key"].ToString();
        command.IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key;
        command.RequestMethod = httpRequest.Method;
        command.RequestPath = httpRequest.Path.Value ?? "/transactions/transfer";
        var result = await sender.Send(command);
        if (result.StatusCode == StatusCodes.Status201Created)
        {
            using var body = JsonDocument.Parse(result.Body);
            httpRequest.HttpContext.Response.Headers.Location =
                $"/transactions/{body.RootElement.GetProperty("id").GetGuid()}";
        }

        return Results.Content(result.Body, "application/json", statusCode: result.StatusCode);
    }

    [EndpointSummary("Create an FX conversion")]
    [EndpointDescription("Converts cash from one Open account to an Open account in another currency of the same customer, at the stored reference rate. Requires Idempotency-Key and expectedRate.")]
    public static async Task<IResult> CreateFxConversion(
        ISender sender,
        HttpRequest httpRequest,
        CreateFxConversionCommand command)
    {
        var key = httpRequest.Headers["Idempotency-Key"].ToString();
        command.IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key;
        command.RequestMethod = httpRequest.Method;
        command.RequestPath = httpRequest.Path.Value ?? "/transactions/fx";
        var result = await sender.Send(command);
        if (result.StatusCode == StatusCodes.Status201Created)
        {
            using var body = JsonDocument.Parse(result.Body);
            httpRequest.HttpContext.Response.Headers.Location =
                $"/transactions/{body.RootElement.GetProperty("id").GetGuid()}";
        }

        return Results.Content(result.Body, "application/json", statusCode: result.StatusCode);
    }

    [EndpointSummary("Quote an FX conversion")]
    [EndpointDescription("Returns the stored rate, its as-of date and both amounts for a conversion. Writes nothing and holds no rate.")]
    public static async Task<Ok<FxConversionQuoteDto>> GetFxConversionQuote(
        ISender sender,
        GetFxConversionQuoteQuery query)
    {
        var quote = await sender.Send(query);
        return TypedResults.Ok(quote);
    }

    [EndpointSummary("Reverse a transaction")]
    [EndpointDescription("Posts an opposite cash transaction. Requires Idempotency-Key. Does not change the original row.")]
    public static async Task<IResult> ReverseTransaction(ISender sender, HttpRequest httpRequest, Guid id)
    {
        var key = httpRequest.Headers["Idempotency-Key"].ToString();
        var result = await sender.Send(new ReverseTransactionCommand
        {
            Id = id,
            IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key,
            RequestMethod = httpRequest.Method,
            RequestPath = httpRequest.Path.Value ?? $"/transactions/{id}/reverse"
        });
        return Results.Content(result.Body, "application/json", statusCode: result.StatusCode);
    }
}
