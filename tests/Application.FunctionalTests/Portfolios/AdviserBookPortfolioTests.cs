using System.Net;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

/// <summary>Portfolios amendment 2026-11-13 part 2: Adviser Book on portfolio-wide commands (PB-T1–PB-T11).</summary>
public class AdviserBookPortfolioTests : TestBase
{
    [Test]
    public async Task BulkRebalance_SharedPortfolio_AdviserEnqueuesOnlyTheBook()
    {
        var world = await AdviserBookWorld.CreateAsync("book-bulk-shared");
        await world.ShareAsync();
        var before = AdviserBookWorld.Scheduler.Enqueued.Count;

        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/portfolios/{world.PortfolioId}/rebalance", world.AdviserA.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AdviserBookWorld.AcceptedAccountsAsync(response)).ShouldBe([world.AccountA]);
        AdviserBookWorld.EnqueuedSince(before).ShouldBe([world.AccountA]);
    }

    [Test]
    public async Task BulkRebalance_OnlyOutsideTheBook_AdviserGetsAnEmptyList()
    {
        var world = await AdviserBookWorld.CreateAsync("book-bulk-outside");
        await world.AssignAsync(world.AdviserB, world.AccountB);
        var before = AdviserBookWorld.Scheduler.Enqueued.Count;

        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/portfolios/{world.PortfolioId}/rebalance", world.AdviserA.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AdviserBookWorld.AcceptedAccountsAsync(response)).ShouldBeEmpty();
        AdviserBookWorld.EnqueuedSince(before).ShouldBeEmpty();
    }

    [Test]
    public async Task BulkRebalance_SharedPortfolio_TenantAdminEnqueuesEveryAssignment()
    {
        var world = await AdviserBookWorld.CreateAsync("book-bulk-admin");
        await world.ShareAsync();
        var before = AdviserBookWorld.Scheduler.Enqueued.Count;

        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/portfolios/{world.PortfolioId}/rebalance", world.TenantAdmin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AdviserBookWorld.AcceptedAccountsAsync(response)).ShouldBe([world.AccountA, world.AccountB], ignoreOrder: true);
        AdviserBookWorld.EnqueuedSince(before).ShouldBe([world.AccountA, world.AccountB], ignoreOrder: true);
    }
}
