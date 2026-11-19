using System.Net;
using System.Text.Json;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Adjustments R17, R18: reversing an adjustment needs <c>transactions.adjust</c>; otherwise an ordinary reverse.</summary>
[Parallelizable]
public class AdjustmentReverseTests : TestBase
{
    private const string LaterActivityMessage =
        "A later transaction changed this holding. Reverse the later transactions first.";

    [Test]
    public async Task TenantAdmin_ReversesAnAdjustment_WithOppositeLegsAndTheSameReason()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);
        var cashBefore = await world.CashAsync();
        var adjustment = await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId,
            amount: -0.60m,
            security: AdjustmentWorld.Holding(world.InstrumentId, 4m, 100.60m),
            reason: "CorporateAction")));

        var response = await ReverseAsync(world.Admin, adjustment);

        var reversalId = await ReadIdAsync(response);
        (await world.CashAsync()).ShouldBe(cashBefore);
        (await world.HoldingAsync()).ShouldBeNull();
        using var reversal = await GetTransactionAsync(world.Admin, reversalId);
        var root = reversal.RootElement;
        root.GetProperty("type").GetString().ShouldBe("Reversal");
        root.GetProperty("originalTransactionId").GetGuid().ShouldBe(adjustment);
        root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("Manual");
        root.GetProperty("adjustment").GetProperty("reason").GetString().ShouldBe("CorporateAction");
        root.GetProperty("cash").EnumerateArray().ShouldHaveSingleItem().GetProperty("amount").GetDecimal().ShouldBe(0.60m);
        var leg = root.GetProperty("security").EnumerateArray().ShouldHaveSingleItem();
        leg.GetProperty("quantity").GetDecimal().ShouldBe(-4m);
        leg.GetProperty("cost").GetDecimal().ShouldBe(-100.60m);
        using var original = await GetTransactionAsync(world.Admin, adjustment);
        original.RootElement.GetProperty("reversedByTransactionId").GetGuid().ShouldBe(reversalId);
    }

    [Test]
    public async Task TheReversalOfAnAdjustment_CannotBeReversed()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var adjustment = await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m)));
        var reversal = await ReadIdAsync(await ReverseAsync(world.Admin, adjustment));

        (await ReverseAsync(world.Admin, reversal)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Adviser_ReversesAnAdjustment_Returns403AndWritesNothing()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var adjustment = await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m)));
        var countBefore = await TransactionCountAsync(world.AccountId);
        var key = Guid.NewGuid();

        var response = await TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{adjustment}/reverse", world.AdviserToken, idempotencyKey: key);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(countBefore);
        (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
        (await world.CashAsync()).ShouldBe(5m);
        using var item = await GetTransactionAsync(world.Admin, adjustment);
        item.RootElement.GetProperty("reversedByTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Adviser_ReversesAnotherTypeOnTheSameAccount_Returns201()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);
        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m)));
        var transferIn = await ReadIdAsync(await FundAsync(world.AdviserToken, world.AccountId, 20m));

        var response = await ReverseAsync(world.AdviserToken, transferIn);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AnAdjustmentWithASecurityLeg_BlocksTheReverseOfAnEarlierBuy()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(1000m);
        var buy = await world.BuyAsync(10m);
        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 0m, 0.60m))));

        var response = await ReverseAsync(world.Admin, buy);

        (await ErrorsAsync(response)).Values.SelectMany(messages => messages).ShouldContain(LaterActivityMessage);
    }

    [Test]
    public async Task AnAdjustmentWithASecurityLeg_IsReversibleOnlyWhileItIsTheLatestSecurityActivity()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(1000m);
        var adjustment = await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 10m, 250m))));
        var laterBuy = await world.BuyAsync(2m);

        var blocked = await ReverseAsync(world.Admin, adjustment);
        (await ErrorsAsync(blocked)).Values.SelectMany(messages => messages).ShouldContain(LaterActivityMessage);

        await ReadIdAsync(await ReverseAsync(world.Admin, laterBuy));
        await ReadIdAsync(await ReverseAsync(world.Admin, adjustment));
        (await world.HoldingAsync()).ShouldBeNull();
    }

    [Test]
    public async Task OtherTenantsAdjustment_Returns404OnReverse_ForTenantAdminAndAdviser()
    {
        var north = await AdjustmentWorld.CreateAsync("adjust-north");
        var south = await AdjustmentWorld.CreateAsync("adjust-south");
        var adjustment = await ReadIdAsync(await south.AdjustAsync(AdjustmentWorld.Body(south.AccountId, amount: 5m)));

        (await ReverseAsync(north.Admin, adjustment)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReverseAsync(north.AdviserToken, adjustment)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionCountAsync(south.AccountId)).ShouldBe(1);
    }
}
