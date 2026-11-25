using System.Net;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.AdjustmentRequests;

/// <summary>Adjustment review R17 (account-closure R5): an account closure waits for the Account's open adjustment
/// requests.</summary>
[Parallelizable]
public class AdjustmentRequestClosureTests : TestBase
{
    private const string OpenRequestMessage =
        "Open adjustment requests must be approved, rejected or cancelled before the account can be closed.";

    [Test]
    public async Task AClosure_WithAnOpenRequest_Returns400AtAccountId_AndStartsNoClosure()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: -5m));

        var response = await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId,
            AccountClosureHttp.Body(world.Withdrawals.NominatedBankAccountId));

        (await ErrorsAsync(response))["AccountId"].ShouldBe([OpenRequestMessage]);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task AClosure_AfterTheRequestsAreDecided_IsRequested()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var rejected = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: -5m));
        var cancelled = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: -6m));
        var approved = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: -7m));
        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, rejected, "reject", "Wrong")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AdjustmentRequestHttp.ActAsync(world.TenantAdmin, cancelled, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, approved, "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId,
            AccountClosureHttp.Body(world.Withdrawals.NominatedBankAccountId));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AnOpenRequestOnAnotherAccount_DoesNotHoldTheClosure()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var spare = await OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Spare", "USD");
        await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, Transactions.AdjustmentWorld.Body(spare, amount: 5m));

        var response = await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId,
            AccountClosureHttp.Body(world.Withdrawals.NominatedBankAccountId));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }
}
