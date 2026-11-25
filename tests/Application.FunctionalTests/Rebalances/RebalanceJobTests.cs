using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R12 (docs/features/rebalances.md §10, the job): a weight change and a bulk request propose per
/// account, with the handle as the rebalance's id; a rebalance in progress gets a follow-up when it ends.</summary>
[Parallelizable]
public class RebalanceJobTests : TestBase
{
    [Test]
    public async Task AWeightChange_Answers202WithHandles_AndTheJobProposesWithTheHandleAsItsId()
    {
        var world = await RebalanceWorld.CreateAsync();

        var handle = await ChangeWeightsAsync(world);
        await RunJobAsync(world, handle);

        var item = await RebalanceHttp.GetAsync(world.TenantAdmin, handle);
        item.GetProperty("trigger").GetString().ShouldBe("Portfolio");
        item.GetProperty("status").GetString().ShouldBe("Proposed");
        item.GetProperty("proposedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("targets").EnumerateArray().Select(target => target.GetProperty("targetWeight").GetDecimal())
            .ShouldBe([50m, 50m]);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task ABulkRequest_ProposesForTheAccountWithTheHandle()
    {
        var world = await RebalanceWorld.CreateAsync();
        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/portfolios/{world.PortfolioId}/rebalance", world.Adviser);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var handle = Handle(await response.Content.ReadAsStringAsync(), world.AccountId);

        await RunJobAsync(world, handle);

        (await RebalanceHttp.RebalanceAsync(handle)).Trigger.ShouldBe(RebalanceTrigger.Portfolio);
    }

    [Test]
    public async Task AnAccountWithAProposal_GetsANewerOne()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, first) = await world.ProposeAsync();

        var handle = await ChangeWeightsAsync(world);
        await RunJobAsync(world, handle);

        var older = await RebalanceHttp.RebalanceAsync(first);
        (older.Status, older.CancelReason).ShouldBe((RebalanceStatus.Cancelled, (RebalanceCancelReason?)RebalanceCancelReason.Superseded));
        (await RebalanceHttp.RebalanceAsync(handle)).Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task AnAccountInProgress_GetsAFollowUp_ProposedWhenTheRebalanceEnds()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);

        var handle = await ChangeWeightsAsync(world);
        await RunJobAsync(world, handle);

        var marked = await RebalanceHttp.RebalanceAsync(id);
        (marked.Status, marked.FollowUp).ShouldBe((RebalanceStatus.Buying, true));
        (await RebalanceHttp.RebalancesOfAsync(world.AccountId)).Count.ShouldBe(1);
        (await RebalanceHttp.GetAsync(world.Adviser, id)).GetProperty("followUp").GetBoolean().ShouldBeTrue();

        for (var run = 0; run < 3; run++)
        {
            await RebalanceHttp.FillWaveAsync(id);
        }

        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Completed);
        var rebalances = await RebalanceHttp.RebalancesOfAsync(world.AccountId);
        rebalances.Count.ShouldBe(2);
        var followUp = rebalances[1];
        followUp.PublicId.ShouldNotBe(handle);
        followUp.Trigger.ShouldBe(RebalanceTrigger.FollowUp);
        followUp.ProposedByUserId.ShouldBeNull();
        followUp.Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task AFollowUp_IsAlsoProposed_WhenThePersonCancelsTheRebalance()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await RunJobAsync(world, await ChangeWeightsAsync(world));

        (await RebalanceHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var rebalances = await RebalanceHttp.RebalancesOfAsync(world.AccountId);
        rebalances.Select(row => (row.Trigger, row.Status)).ShouldBe([
            (RebalanceTrigger.Account, RebalanceStatus.Cancelled),
            (RebalanceTrigger.FollowUp, RebalanceStatus.Proposed)
        ]);
    }

    [Test]
    public async Task NoFollowUp_WhenTheAssignmentWasRemoved()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BrokerHttp.SendAsync();
        await RunJobAsync(world, await ChangeWeightsAsync(world));
        (await PortfolioHttp.Send(HttpMethod.Delete, $"/accounts/{world.AccountId}/portfolio", world.Adviser)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        await BrokerHttp.CancelAsync();
        foreach (var order in await RebalanceHttp.OrdersOfAsync(id))
        {
            BrokerHttp.Feed.AddEnd(order.PublicId, ExecutionReportKind.Cancelled);
        }

        await BrokerHttp.RunReportsAsync();
        await BrokerHttp.AdvanceAsync();

        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Cancelled);
        (await RebalanceHttp.RebalancesOfAsync(world.AccountId)).Count.ShouldBe(1);
    }

    [Test]
    public async Task AFailedPlan_IsStoredFailed_AndTheJobDoesNotThrow()
    {
        var world = await RebalanceWorld.CreateAsync();
        var handle = await ChangeWeightsAsync(world);
        await RebalanceHttp.DisableInstrumentAsync(world.BetaId);

        await RunJobAsync(world, handle);

        var failed = await RebalanceHttp.RebalanceAsync(handle);
        (failed.Status, failed.FailureCode).ShouldBe((RebalanceStatus.Failed, "instrument_inactive"));
    }

    /// <summary>Changes Demo Growth to 50 % each; answers the handle of the world's account from the 202.</summary>
    private static async Task<Guid> ChangeWeightsAsync(RebalanceWorld world)
    {
        var current = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{world.PortfolioId}", world.TenantAdmin);
        using var json = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{world.PortfolioId}", world.TenantAdmin, new
        {
            name = "Demo Growth",
            rowVersion = json.RootElement.GetProperty("rowVersion").GetString(),
            lines = new[]
            {
                new { instrumentId = world.AcmeId, targetWeight = 50m },
                new { instrumentId = world.BetaId, targetWeight = 50m }
            }
        });
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, content);
        return Handle(content, world.AccountId);
    }

    private static Guid Handle(string content, Guid accountId)
    {
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("accounts").EnumerateArray()
            .Single(item => item.GetProperty("accountId").GetGuid() == accountId)
            .GetProperty("handle").GetGuid();
    }

    /// <summary>Runs the job the 202 enqueued, as the worker does.</summary>
    private static async Task RunJobAsync(RebalanceWorld world, Guid handle)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RebalanceJob>().Execute(world.AccountId, handle);
    }
}
