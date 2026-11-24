using System.Net;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Rebalances;
using MyWealthV2.Application.FunctionalTests.Transactions;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R24, R45: an open transfer keeps the account from closing and from following a portfolio.</summary>
[Parallelizable]
public class TransferAccountGuardTests : TestBase
{
    [Test]
    public async Task AClosure_WithAnOpenTransfer_Returns400AtAccountId_AndAfterTheCancelItIsAccepted()
    {
        var world = await TransferWorld.CreateAsync(holding: 0m);
        var id = await world.RequestAsync();
        var payee = await CustodialHttp.VerifiedPayeeAsync(world.AccountId);

        var refused = await RequestClosureAsync(world, payee);

        (await CustodialHttp.ErrorsAsync(refused))["AccountId"]
            .ShouldBe(["Open transfers of securities must be completed, rejected, cancelled or set aside before the account can be closed."]);
        (await TransferHttp.ActAsync(world.Adviser, id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var accepted = await RequestClosureAsync(world, payee);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AssigningAPortfolio_ToAnAccountWithAnOpenTransfer_Returns400AccountNotEligible()
    {
        var world = await TransferWorld.CreateAsync();
        await world.RequestAsync("Out", 10m);
        var portfolio = await RebalanceHttp.CreatePortfolioAsync(world.TenantAdmin, "USD", (world.InstrumentId, 100m));

        var refused = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/portfolio", world.TenantAdmin,
            new { portfolioId = portfolio });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        var problem = await ProblemResponse.ReadAsync(refused);
        problem.Code.ShouldBe("account_not_eligible");
        problem.Detail.ShouldBe("Complete or cancel the account's open transfers of securities before assigning a portfolio.");
    }

    private static Task<HttpResponseMessage> RequestClosureAsync(TransferWorld world, Guid payee) =>
        TransactionHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/closures", world.Adviser,
            new Dictionary<string, object?> { ["nominatedBankAccountId"] = payee, ["memo"] = "Customer is leaving" }, Guid.NewGuid());
}
