using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.InKindTransfers;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R28–R31, R35, R40: the new shares of a reinvestment, their allocation and posting, and paying
/// in cash when no shares arrive.</summary>
[Parallelizable]
public class DividendSharesTests : TestBase
{
    // 78.82352941 shares of Ned take part; 67.00 is reinvested and the custodian pays 93.50 − 67.00 in cash.
    private const decimal Cash = 26.50m;

    private sealed record Plan(DividendWorld Dividend, SafekeepingAccount Safekeeping);

    private static async Task<Plan> AcceptedAsync(bool olgaReinvests = false)
    {
        SafekeepingAccount? safekeeping = null;
        var dividend = await DividendWorld.CreateAsync(price: 25m, setUp: async (world, holders) =>
        {
            safekeeping = await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "USD");
            await DividendReinvestmentTests.ElectAsync(world, holders.Ned);
            if (olgaReinvests)
            {
                await DividendReinvestmentTests.ElectAsync(world, holders.Olga);
            }
        });
        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SendReinvestmentElections>().RunAsync(SafekeepingHttp.Connection);
        }

        (await dividend.DividendAsync()).ElectionStatus.ShouldBe(ReinvestmentElectionStatus.Accepted);
        return new Plan(dividend, safekeeping!);
    }

    private static async Task DeliverAsync(Plan plan, decimal shares, CustodyMovementDirection direction = CustodyMovementDirection.In)
    {
        TransferHttp.Feed.AddMovement(plan.Safekeeping.AccountNumber, direction, "ACME.SIM", shares,
            kind: CustodyMovementKind.CorporateAction, corporateActionId: plan.Dividend.PartyActionId);
        await TransferHttp.PullAndProcessAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheReceiptAndTheShares_PayTheDividend_InEitherOrder_AndTheReinvestmentIsPostedAtItsCost(bool sharesFirst)
    {
        var plan = await AcceptedAsync();
        var dividend = plan.Dividend;
        var costBefore = await HoldingCostAsync(dividend, dividend.Holders.Ned);
        if (sharesFirst)
        {
            await DeliverAsync(plan, 2.68m);
            (await dividend.DividendAsync()).PaidAt.ShouldBeNull();
            await dividend.ReceiveAsync(Cash);
        }
        else
        {
            await dividend.ReceiveAsync(Cash);
            (await dividend.DividendAsync()).PaidAt.ShouldBeNull();
            await DeliverAsync(plan, 2.68m);
        }

        var paid = await dividend.DividendAsync();
        (paid.SharesReceived, paid.PaidAt is not null).ShouldBe(((decimal?)2.68m, true));
        (await RunApplyAsync()).Posted.ShouldBe(2);

        var ned = await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Ned, dividend.DividendId);
        ned.GetProperty("cash").EnumerateArray().Select(leg => (leg.GetProperty("role").GetString(), leg.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Principal", 100.00m), ("Tax", -15.00m), ("Tax", -18.00m), ("Reinvestment", -67.00m)], ignoreOrder: true);
        var security = ned.GetProperty("security").EnumerateArray().Single();
        (security.GetProperty("quantity").GetDecimal(), security.GetProperty("cost").GetDecimal()).ShouldBe((2.68m, 67.00m));
        ned.GetProperty("dividend").GetProperty("reinvestedQuantity").GetDecimal().ShouldBe(2.68m);
        ned.GetProperty("dividend").GetProperty("reinvestmentPrice").GetDecimal().ShouldBe(25m);
        var holding = await HoldingAsync(dividend, dividend.Holders.Ned);
        holding.GetProperty("quantity").GetDecimal().ShouldBe(102.68m);
        holding.GetProperty("cost").GetDecimal().ShouldBe(costBefore + 67.00m);

        var entitlement = (await EntitlementsAsync(dividend.DividendId)).Single(row => row.ReinvestedQuantity is not null);
        (entitlement.ReinvestedQuantity, entitlement.NetAmount).ShouldBe(((decimal?)2.68m, (decimal?)67.00m));
        var election = (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("election");
        election.GetProperty("sharesReceived").GetDecimal().ShouldBe(2.68m);
    }

    [Test]
    public async Task TheDeliveredShares_AreSharedOutExactly_OverTheReinvestingAccounts()
    {
        var plan = await AcceptedAsync(olgaReinvests: true);
        var dividend = plan.Dividend;

        await DeliverAsync(plan, 2.94m);
        await dividend.ReceiveAsync(93.50m - 73.70m);

        var reinvested = (await EntitlementsAsync(dividend.DividendId)).Select(row => row.ReinvestedQuantity ?? 0m).ToList();
        reinvested.Sum().ShouldBe(2.94m);
        reinvested.ShouldAllBe(quantity => quantity > 0m);
    }

    [Test]
    public async Task AHoldingSoldSinceTheExDate_IsCreatedAgain_ByTheNewShares()
    {
        var plan = await AcceptedAsync();
        var dividend = plan.Dividend;
        await CustodialHttp.InsertManualTradeAsync(dividend.Holders.Ned, "Sell", dividend.Holders.InstrumentId, 100m);
        await DeliverAsync(plan, 2.68m);
        await dividend.ReceiveAsync(Cash);

        (await RunApplyAsync()).Posted.ShouldBe(2);

        (await HoldingAsync(dividend, dividend.Holders.Ned)).GetProperty("quantity").GetDecimal().ShouldBe(2.68m);
    }

    [Test]
    public async Task AMovementOut_IsMatchedAndNotUsed()
    {
        var plan = await AcceptedAsync();

        await DeliverAsync(plan, 2.68m, CustodyMovementDirection.Out);

        (await plan.Dividend.DividendAsync()).SharesReceived.ShouldBeNull();
    }

    [Test]
    public async Task PayInCash_ReleasesTheElection_AndEveryAccountIsPaidInCash()
    {
        var plan = await AcceptedAsync();
        var dividend = plan.Dividend;
        await dividend.ReceiveAsync(Cash);
        var rowVersion = await RowVersionAsync(dividend.TenantAdmin, dividend.DividendId);

        var response = await Send(HttpMethod.Post, $"{Root}/{dividend.DividendId}/pay-in-cash", dividend.TenantAdmin,
            new { rowVersion, note = "The registry issued no shares" });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var election = (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("election");
        election.GetProperty("status").GetString().ShouldBe("Lapsed");
        election.GetProperty("note").GetString().ShouldBe("The registry issued no shares");
        election.GetProperty("releasedAt").ValueKind.ShouldBe(JsonValueKind.String);
        election.GetProperty("releasedBy").GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        (await RunApplyAsync()).Posted.ShouldBe(2);
        (await EntitlementsAsync(dividend.DividendId)).ShouldAllBe(row => row.ReinvestedQuantity == null);
        (await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Ned, dividend.DividendId)).GetProperty("security")
            .GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task PayInCash_OfADividendThatDoesNotWaitForShares_Returns400_AStaleRowVersion409_AndAnAdviser403()
    {
        var plan = await AcceptedAsync();
        var dividend = plan.Dividend;
        var path = $"{Root}/{dividend.DividendId}/pay-in-cash";

        // No receipt yet.
        var early = await Send(HttpMethod.Post, path, dividend.TenantAdmin,
            new { rowVersion = await RowVersionAsync(dividend.TenantAdmin, dividend.DividendId), note = "No shares" });
        (await ErrorsAsync(early))["Status"].ShouldBe(["This dividend does not wait for shares."]);

        await dividend.ReceiveAsync(Cash);
        (await Send(HttpMethod.Post, path, dividend.TenantAdmin, new { rowVersion = StaleRowVersion, note = "No shares" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorsAsync(await Send(HttpMethod.Post, path, dividend.TenantAdmin,
                new { rowVersion = await RowVersionAsync(dividend.TenantAdmin, dividend.DividendId), note = " " })))
            .ShouldContainKey("Note");
        (await Send(HttpMethod.Post, path, dividend.World.AdviserToken.AccessToken,
                new { rowVersion = await RowVersionAsync(dividend.TenantAdmin, dividend.DividendId), note = "No shares" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await DeliverAsync(plan, 2.68m);
        var late = await Send(HttpMethod.Post, path, dividend.TenantAdmin,
            new { rowVersion = await RowVersionAsync(dividend.TenantAdmin, dividend.DividendId), note = "No shares" });
        (await ErrorsAsync(late))["Status"].ShouldBe(["This dividend does not wait for shares."]);
    }

    private static async Task<JsonElement> HoldingAsync(DividendWorld dividend, Guid accountId) =>
        (await GetAsync(dividend.TenantAdmin, $"/accounts/{accountId}/holdings")).GetProperty("items").EnumerateArray()
        .Single(row => row.GetProperty("instrumentId").GetGuid() == dividend.Holders.InstrumentId);

    private static async Task<decimal> HoldingCostAsync(DividendWorld dividend, Guid accountId) =>
        (await HoldingAsync(dividend, accountId)).GetProperty("cost").GetDecimal();

    private static async Task<JsonElement> PostingOfAsync(string token, Guid accountId, Guid actionId) =>
        (await GetAsync(token, $"/transactions?accountId={accountId}&pageSize=100")).GetProperty("items").EnumerateArray().Single(row =>
            row.GetProperty("source").GetProperty("kind").GetString() == "CorporateAction"
            && row.GetProperty("source").GetProperty("id").GetGuid() == actionId);
}
