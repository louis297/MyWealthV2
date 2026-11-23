using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R10, R22–R25 (docs/features/rebalances.md §10, assignment): assign proposes; unassign,
/// disable and assigning another portfolio deal with the open rebalance; the assignment item shows the newest
/// rebalance.</summary>
public class AssignmentRebalanceTests : TestBase
{
    [Test]
    public async Task Assign_WritesTheAssignment_AndAnswersAProposal()
    {
        var world = await RebalanceWorld.CreateAsync(assign: false);

        var response = await AssignAsync(world, world.PortfolioId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Proposed");
        var id = json.RootElement.GetProperty("rebalanceId").GetGuid();
        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("trigger").GetString().ShouldBe("Assign");
        item.GetProperty("status").GetString().ShouldBe("Proposed");
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
        (await AssignmentAsync(world)).GetProperty("status").GetString().ShouldBe("Active");
    }

    [Test]
    public async Task Assign_WithAPlanThatFails_Returns400WithItsCode_AndLeavesTheAssignmentActive()
    {
        var world = await RebalanceWorld.CreateAsync(assign: false, betaPriceDate: OrderHttp.Today.AddDays(-5));

        var response = await AssignAsync(world, world.PortfolioId);

        (await RebalanceHttp.CodeAsync(response)).ShouldBe("stale_price");
        var assignment = await AssignmentAsync(world);
        assignment.GetProperty("status").GetString().ShouldBe("Active");
        assignment.GetProperty("rebalance").GetProperty("status").GetString().ShouldBe("Failed");
        assignment.GetProperty("rebalance").GetProperty("failure").GetProperty("code").GetString().ShouldBe("stale_price");
    }

    [Test]
    public async Task TheAssignmentItem_CarriesItsNewestRebalance_AndNotTheThreeOldFields()
    {
        var world = await RebalanceWorld.CreateAsync();
        var none = await AssignmentAsync(world);
        none.GetProperty("rebalance").ValueKind.ShouldBe(JsonValueKind.Null);
        none.TryGetProperty("lastRebalanceStatus", out _).ShouldBeFalse();
        none.TryGetProperty("lastRebalanceMessage", out _).ShouldBeFalse();
        none.TryGetProperty("lastRebalanceAt", out _).ShouldBeFalse();
        await world.ProposeAsync();
        var (_, newest) = await world.ProposeAsync();

        var rebalance = (await AssignmentAsync(world)).GetProperty("rebalance");

        rebalance.GetProperty("id").GetGuid().ShouldBe(newest);
        rebalance.GetProperty("status").GetString().ShouldBe("Proposed");
        rebalance.GetProperty("failure").ValueKind.ShouldBe(JsonValueKind.Null);
        rebalance.GetProperty("proposedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        rebalance.GetProperty("endedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Unassign_WithAProposal_CancelsItUnassigned()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();

        (await UnassignAsync(world)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        (rebalance.Status, rebalance.CancelReason).ShouldBe((RebalanceStatus.Cancelled, (RebalanceCancelReason?)RebalanceCancelReason.Unassigned));
    }

    [Test]
    public async Task Unassign_InProgress_IsNotRefused_AndThePlatformRequestsTheCancel()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BrokerHttp.SendAsync();

        (await UnassignAsync(world)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var requested = await RebalanceHttp.RebalanceAsync(id);
        requested.Status.ShouldBe(RebalanceStatus.Buying);
        requested.CancelRequestedAt.ShouldNotBeNull();
        requested.CancelRequestedByUserId.ShouldBeNull();
        (await RebalanceHttp.OrdersOfAsync(id)).ShouldAllBe(order => order.HasCancelRequest && order.CancelRequestedByUserId == null);

        await BrokerHttp.CancelAsync();
        foreach (var order in await RebalanceHttp.OrdersOfAsync(id))
        {
            BrokerHttp.Feed.AddEnd(order.PublicId, ExecutionReportKind.Cancelled);
        }

        await BrokerHttp.RunReportsAsync();
        await BrokerHttp.AdvanceAsync();
        var ended = await RebalanceHttp.RebalanceAsync(id);
        (ended.Status, ended.CancelReason).ShouldBe((RebalanceStatus.Cancelled, (RebalanceCancelReason?)RebalanceCancelReason.Unassigned));
    }

    [Test]
    public async Task DisablingThePortfolio_CancelsTheOpenRebalanceOfEachAccount()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        var current = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{world.PortfolioId}", world.TenantAdmin);
        using var json = JsonDocument.Parse(await current.Content.ReadAsStringAsync());

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/portfolios/{world.PortfolioId}/disable", world.TenantAdmin,
            new { rowVersion = json.RootElement.GetProperty("rowVersion").GetString() });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        (rebalance.Status, rebalance.CancelReason).ShouldBe((RebalanceStatus.Cancelled, (RebalanceCancelReason?)RebalanceCancelReason.Unassigned));
    }

    [Test]
    public async Task AssigningAnotherPortfolio_WithAProposal_ReplacesItAndProposesForTheNewOne()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, first) = await world.ProposeAsync();
        var other = await RebalanceHttp.CreatePortfolioAsync(world.TenantAdmin, "USD", (world.AcmeId, 100m));

        var response = await AssignAsync(world, other);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var old = await RebalanceHttp.RebalanceAsync(first);
        (old.Status, old.CancelReason).ShouldBe((RebalanceStatus.Cancelled, (RebalanceCancelReason?)RebalanceCancelReason.Unassigned));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = await RebalanceHttp.GetAsync(world.Adviser, json.RootElement.GetProperty("rebalanceId").GetGuid());
        item.GetProperty("portfolio").GetProperty("id").GetGuid().ShouldBe(other);
        item.GetProperty("status").GetString().ShouldBe("Proposed");
    }

    [Test]
    public async Task AssigningAnotherPortfolio_InProgress_Returns400RebalanceInProgress_AndWritesNothing()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        var other = await RebalanceHttp.CreatePortfolioAsync(world.TenantAdmin, "USD", (world.AcmeId, 100m));

        var response = await AssignAsync(world, other);

        (await RebalanceHttp.CodeAsync(response)).ShouldBe("rebalance_in_progress");
        (await AssignmentAsync(world)).GetProperty("portfolioId").GetGuid().ShouldBe(world.PortfolioId);
        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Buying);
    }

    [Test]
    public async Task Propose_InProgress_Returns400RebalanceInProgress()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);

        var response = await RebalanceHttp.ProposeAsync(world.Adviser, world.AccountId);

        (await RebalanceHttp.CodeAsync(response)).ShouldBe("rebalance_in_progress");
        (await RebalanceHttp.DetailAsync(response)).ShouldBe("A rebalance of this account is in progress. Cancel it or wait until it has ended.");
        (await RebalanceHttp.RebalancesOfAsync(world.AccountId)).Count.ShouldBe(1);
    }

    private static Task<HttpResponseMessage> AssignAsync(RebalanceWorld world, Guid portfolioId) =>
        PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/portfolio", world.Adviser, new { portfolioId });

    private static Task<HttpResponseMessage> UnassignAsync(RebalanceWorld world) =>
        PortfolioHttp.Send(HttpMethod.Delete, $"/accounts/{world.AccountId}/portfolio", world.Adviser);

    private static async Task<JsonElement> AssignmentAsync(RebalanceWorld world)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{world.AccountId}/portfolio", world.Adviser);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
