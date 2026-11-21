using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>Withdrawals R44: the payout fields on the statement line item and the <c>direction</c> filter.</summary>
[Parallelizable]
public class StatementLinePayoutReadTests : TestBase
{
    [Test]
    public async Task APayoutLine_AndItsReturn_ShowTheWithdrawal_TheMethod_AndTheReturnedLine()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (withdrawalId, payoutOutsideId) = await world.PaidAsync(300m);
        var returnOutsideId = world.AddReturnLine(payoutOutsideId, 300m, "Account closed");
        await BankFeedHttp.RunAsync();
        var payoutLine = await BankFeedHttp.LineAsync(payoutOutsideId);
        var returnLine = await BankFeedHttp.LineAsync(returnOutsideId);
        var withdrawal = await WithdrawalHttp.GetAsync(world.TenantAdmin, withdrawalId);

        var payout = await StatementLineHttp.GetAsync(world.TenantAdmin, payoutLine.PublicId);
        payout.GetProperty("direction").GetString().ShouldBe("Debit");
        payout.GetProperty("status").GetString().ShouldBe("Matched");
        payout.GetProperty("withdrawalId").GetGuid().ShouldBe(withdrawalId);
        payout.GetProperty("relatedLineId").ValueKind.ShouldBe(JsonValueKind.Null);
        payout.GetProperty("returnReason").ValueKind.ShouldBe(JsonValueKind.Null);
        payout.GetProperty("match").GetProperty("method").GetString().ShouldBe("Payment");
        payout.GetProperty("match").GetProperty("transactionId").GetGuid().ShouldBe(withdrawal.GetProperty("payoutTransactionId").GetGuid());
        payout.GetProperty("match").GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);

        var returned = await StatementLineHttp.GetAsync(world.TenantAdmin, returnLine.PublicId);
        returned.GetProperty("direction").GetString().ShouldBe("Credit");
        returned.GetProperty("withdrawalId").GetGuid().ShouldBe(withdrawalId);
        returned.GetProperty("relatedLineId").GetGuid().ShouldBe(payoutLine.PublicId);
        returned.GetProperty("returnReason").GetString().ShouldBe("Account closed");
        returned.GetProperty("match").GetProperty("method").GetString().ShouldBe("PaymentReturn");
        returned.GetProperty("match").GetProperty("transactionId").GetGuid().ShouldBe(withdrawal.GetProperty("returnTransactionId").GetGuid());
    }

    [Test]
    public async Task Direction_FiltersTheList_AndAnUnknownOneIs400()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (_, payoutOutsideId) = await world.PaidAsync(300m);
        var payoutLine = await BankFeedHttp.LineAsync(payoutOutsideId);

        (await StatementLineHttp.ListIdsAsync(world.TenantAdmin, "?direction=Debit")).ShouldBe([payoutLine.PublicId]);
        (await StatementLineHttp.ListIdsAsync(world.TenantAdmin, "?direction=credit")).ShouldNotContain(payoutLine.PublicId);
        (await StatementLineHttp.ListIdsAsync(world.TenantAdmin, "?direction=Credit")).ShouldNotBeEmpty();
        (await CustodialHttp.ErrorsAsync(await StatementLineHttp.Send(HttpMethod.Get, StatementLineHttp.Path + "?direction=Sideways", world.TenantAdmin)))
            .ShouldContainKey("Direction");
    }

    [Test]
    public async Task AReturnedPayoutLine_CannotBeUnmatchedByHand()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (_, payoutOutsideId) = await world.PaidAsync(300m);
        var returnOutsideId = world.AddReturnLine(payoutOutsideId, 300m);
        await BankFeedHttp.RunAsync();
        var returnLine = await BankFeedHttp.LineAsync(returnOutsideId);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin, returnLine.PublicId, "unmatch", new() { ["note"] = "Wrong" });

        (await CustodialHttp.ErrorsAsync(response))["Status"].ShouldBe(["This line returned a payment and cannot be unmatched."]);
        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
    }

    [Test]
    public async Task APayoutLine_OffersNoAction()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var (_, payoutOutsideId) = await world.PaidAsync(300m);
        var payoutLine = await BankFeedHttp.LineAsync(payoutOutsideId);

        (await CustodialHttp.ErrorsAsync(await StatementLineHttp.ActAsync(world.TenantAdmin, payoutLine.PublicId, "unmatch", new() { ["note"] = "x" })))
            ["Status"].ShouldBe(["This line is a payment out and cannot be unmatched."]);
    }
}
