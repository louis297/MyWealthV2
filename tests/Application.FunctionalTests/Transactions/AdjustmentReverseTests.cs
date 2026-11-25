using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Adjustment review R15 (replaces adjustments R17, R18): an <c>Adjustment</c> is never reversed by hand. A
/// wrong one is corrected by a request for the opposite change.</summary>
[Parallelizable]
public class AdjustmentReverseTests : TestBase
{
    private const string NoReverseMessage = "An adjustment cannot be reversed. Request a new adjustment that corrects it.";

    private const string LaterActivityMessage =
        "A later transaction changed this holding. Reverse the later transactions first.";

    [Test]
    public async Task Reverse_OfAnAdjustmentPostedFromARequest_Returns400_ForTheTenantAdminAndTheAdviser_AndWritesNothing()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var adjustment = await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m)));
        var countBefore = await TransactionCountAsync(world.AccountId);

        foreach (var token in new[] { world.Admin, world.AdviserToken })
        {
            var key = Guid.NewGuid();
            var response = await TransactionHttp.Send(HttpMethod.Post, $"/transactions/{adjustment}/reverse", token, idempotencyKey: key);

            (await ErrorsAsync(response))[string.Empty].ShouldBe([NoReverseMessage]);
            (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
        }

        (await TransactionCountAsync(world.AccountId)).ShouldBe(countBefore);
        (await world.CashAsync()).ShouldBe(5m);
        using var item = await GetTransactionAsync(world.Admin, adjustment);
        item.RootElement.GetProperty("reversedByTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Reverse_OfAnAdjustmentWithSourceManual_PostedBeforeTheReview_Returns400WithTheSameMessage()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var adjustment = await InsertManualAdjustmentAsync(world.AccountId, 5m);

        foreach (var token in new[] { world.Admin, world.AdviserToken })
        {
            (await ErrorsAsync(await ReverseAsync(token, adjustment)))[string.Empty].ShouldBe([NoReverseMessage]);
        }

        (await world.CashAsync()).ShouldBe(5m);
    }

    [Test]
    public async Task AWrongAdjustment_IsCorrectedByARequestForTheOppositeChange()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);
        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, amount: -0.60m, security: AdjustmentWorld.Holding(world.InstrumentId, 4m, 100.60m), reason: "CorporateAction")));

        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, amount: 0.60m, security: AdjustmentWorld.Holding(world.InstrumentId, -4m, -100.60m), reason: "EntryError")));

        (await world.CashAsync()).ShouldBe(100m);
        (await world.HoldingAsync()).ShouldBeNull();
    }

    [Test]
    public async Task Adviser_ReversesAnotherTypeOnTheSameAccount_Returns201()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);
        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m)));
        var transferIn = await InsertManualTransferInAsync(world.AccountId, 20m);

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
    public async Task OtherTenantsAdjustment_Returns404OnReverse_ForTenantAdminAndAdviser()
    {
        var north = await AdjustmentWorld.CreateAsync("adjust-north");
        var south = await AdjustmentWorld.CreateAsync("adjust-south");
        var adjustment = await ReadIdAsync(await south.AdjustAsync(AdjustmentWorld.Body(south.AccountId, amount: 5m)));

        (await ReverseAsync(north.Admin, adjustment)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReverseAsync(north.AdviserToken, adjustment)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionCountAsync(south.AccountId)).ShouldBe(1);
    }

    [Test]
    public async Task TheOldCreateRoute_NoLongerPostsAnAdjustment()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions/adjustment", world.Admin,
            AdjustmentWorld.Body(world.AccountId, amount: 5m), Guid.NewGuid());

        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(400, await response.Content.ReadAsStringAsync());
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
        (await world.CashAsync()).ShouldBe(0m);
    }

    /// <summary>An <c>Adjustment</c> as the create route wrote it before the review: source <c>Manual</c>, no id.</summary>
    private static async Task<Guid> InsertManualAdjustmentAsync(Guid accountPublicId, decimal amount)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        var cash = await db.TransactionCashLegs.IgnoreQueryFilters().Where(leg => leg.AccountId == account.Id).SumAsync(leg => leg.Amount);
        var adjustment = Transaction.PostAdjustment(
            account.TenantId, account.Id, account.Type, account.Currency, Domain.Enums.AdjustmentReason.EntryError,
            "Typed before the review", null, DateTimeOffset.UtcNow, TransactionSource.Manual, amount, cash, null);
        adjustment.CreatedBy = "test";
        adjustment.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(adjustment);
        await db.SaveChangesAsync();
        return adjustment.PublicId;
    }
}
