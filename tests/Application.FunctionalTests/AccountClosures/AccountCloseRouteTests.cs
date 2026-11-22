using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>Account closure R22, R23: the plain close route and a hand <c>CloseOut</c> are refused on a Custodial
/// account; External accounts are unchanged.</summary>
[Parallelizable]
public class AccountCloseRouteTests : TestBase
{
    private const string CloseMessage = "A Custodial account is closed with an account closure.";
    private const string CloseOutMessage =
        "The remainder of a Custodial account is written off by its account closure. Request an account closure.";

    [Test]
    public async Task Close_OnAnOpenCustodialAccount_Returns400_ForEveryCaller_AndTheAccountStaysOpen()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { world.TenantAdmin, world.Adviser, system.AccessToken })
        {
            var errors = await CustodialHttp.ErrorsAsync(await PlainCloseAsync(token, world.AccountId, world.TenantAdmin));

            errors[string.Empty].ShouldBe([CloseMessage]);
        }

        (await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId)).GetProperty("status").GetString().ShouldBe("Open");
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Close_OnAClosedCustodialAccount_Returns204()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId);

        (await PlainCloseAsync(world.TenantAdmin, world.AccountId, world.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Close_OnAnExternalAccount_IsUnchanged()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);

        (await PlainCloseAsync(world.Adviser, external, world.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AccountClosureHttp.AccountAsync(world.TenantAdmin, external)).GetProperty("status").GetString().ShouldBe("Closed");
    }

    [TestCase(0.004, 0.004)]
    [TestCase(5.00, 5.00)]
    public async Task AHandCloseOut_OnACustodialAccount_Returns400OnType(decimal cash, decimal closeOut)
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        await AccountClosureHttp.InsertCashAsync(world.AccountId, cash);

        var errors = await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(
            world.Adviser, CustodialHttp.Cash(world.AccountId, "CloseOut", -closeOut)));

        errors["Type"].ShouldBe([CloseOutMessage]);
        (await CustodialHttp.CashBalanceAsync(world.TenantAdmin, world.AccountId)).ShouldBe(cash);
    }

    [Test]
    public async Task AHandCloseOut_OnAnExternalAccount_Returns201()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.TenantAdmin, external, 5m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CustodialHttp.PostAsync(world.Adviser, CustodialHttp.Cash(external, "CloseOut", -5m).WithBookedAt()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary><c>POST /accounts/{id}/close</c> with the account's current row version, read by
    /// <paramref name="readToken"/>.</summary>
    private static async Task<HttpResponseMessage> PlainCloseAsync(string accessToken, Guid accountId, string readToken)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", readToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        return await TransactionHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
    }
}
