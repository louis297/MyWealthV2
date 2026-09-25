using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Web.Endpoints;

public class Transactions : IEndpointGroup
{
    public static string RoutePrefix => "/transactions";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetTransactions).RequireAuthorization(Policies.TransactionsRead);
        groupBuilder.MapPost(CreateTransaction).RequireAuthorization(Policies.TransactionsCreate);
    }

    [EndpointSummary("List transactions")]
    [EndpointDescription("Returns a paged list of transactions in one tenant.")]
    public static Task<IResult> GetTransactions() =>
        throw new NotImplementedException();

    [EndpointSummary("Create a transaction")]
    [EndpointDescription("Posts a cash transaction on an open account.")]
    public static Task<IResult> CreateTransaction() =>
        throw new NotImplementedException();
}
