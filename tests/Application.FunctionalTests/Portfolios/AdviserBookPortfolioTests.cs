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

    [Test]
    public async Task Update_SharedPortfolio_AdviserNewLines_Returns409AndChangesNothing()
    {
        var world = await AdviserBookWorld.CreateAsync("book-update-lines");
        await world.ShareAsync();
        var rowVersion = await world.RowVersionAsync();
        var before = AdviserBookWorld.Scheduler.Enqueued.Count;

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{world.PortfolioId}", world.AdviserA.AccessToken, new
        {
            name = "Growth",
            rowVersion,
            lines = world.NewLines()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ProblemResponse.ReadAsync(response);
        problem.Code.ShouldBe("portfolio_shared");
        problem.Title.ShouldBe("Portfolio is shared");
        problem.Detail.ShouldBe(
            "This portfolio is assigned to accounts outside your book. Ask a TenantAdmin to change or disable it.");
        var stored = await world.GetPortfolioAsync();
        stored.GetProperty("rowVersion").GetString().ShouldBe(rowVersion);
        stored.GetProperty("lines").GetArrayLength().ShouldBe(1);
        stored.GetProperty("lines")[0].GetProperty("targetWeight").GetDecimal().ShouldBe(100m);
        AdviserBookWorld.EnqueuedSince(before).ShouldBeEmpty();
    }

    [Test]
    public async Task Update_SharedPortfolio_AdviserSettingsOnly_Returns409AndKeepsTheSettings()
    {
        var world = await AdviserBookWorld.CreateAsync("book-update-settings");
        await world.ShareAsync();

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{world.PortfolioId}", world.AdviserA.AccessToken, new
        {
            name = "Growth",
            rowVersion = await world.RowVersionAsync(),
            minimumTrade = 500m,
            lines = world.SameLines()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("portfolio_shared");
        (await world.GetPortfolioAsync()).GetProperty("minimumTrade").GetDecimal().ShouldBe(1m);
    }

    [Test]
    public async Task Update_SharedPortfolio_AdviserStaleRowVersion_ReturnsPortfolioShared()
    {
        var world = await AdviserBookWorld.CreateAsync("book-update-stale");
        await world.ShareAsync();

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{world.PortfolioId}", world.AdviserA.AccessToken, new
        {
            name = "Growth",
            rowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]),
            lines = world.NewLines()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("portfolio_shared");
    }

    [Test]
    public async Task Update_OnlyInTheBook_AdviserNewLines_Returns202()
    {
        var world = await AdviserBookWorld.CreateAsync("book-update-own");
        await world.AssignAsync(world.AdviserA, world.AccountA);
        var before = AdviserBookWorld.Scheduler.Enqueued.Count;

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{world.PortfolioId}", world.AdviserA.AccessToken, new
        {
            name = "Growth",
            rowVersion = await world.RowVersionAsync(),
            lines = world.NewLines()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AdviserBookWorld.AcceptedAccountsAsync(response)).ShouldBe([world.AccountA]);
        AdviserBookWorld.EnqueuedSince(before).ShouldBe([world.AccountA]);
    }

    [Test]
    public async Task Update_SharedPortfolio_TenantAdminNewLines_Returns202ForEveryAssignment()
    {
        var world = await AdviserBookWorld.CreateAsync("book-update-admin");
        await world.ShareAsync();
        var before = AdviserBookWorld.Scheduler.Enqueued.Count;

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{world.PortfolioId}", world.TenantAdmin.AccessToken, new
        {
            name = "Growth",
            rowVersion = await world.RowVersionAsync(),
            lines = world.NewLines()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AdviserBookWorld.AcceptedAccountsAsync(response)).ShouldBe([world.AccountA, world.AccountB], ignoreOrder: true);
        AdviserBookWorld.EnqueuedSince(before).ShouldBe([world.AccountA, world.AccountB], ignoreOrder: true);
    }

    [Test]
    public async Task Disable_SharedPortfolio_Adviser_Returns409AndKeepsEveryAssignment()
    {
        var world = await AdviserBookWorld.CreateAsync("book-disable-shared");
        await world.ShareAsync();

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/portfolios/{world.PortfolioId}/disable", world.AdviserA.AccessToken, new
        {
            rowVersion = await world.RowVersionAsync()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("portfolio_shared");
        (await world.GetPortfolioAsync()).GetProperty("isActive").GetBoolean().ShouldBeTrue();
        (await AssignmentStatusAsync(world.AdviserA, world.AccountA)).ShouldBe(HttpStatusCode.OK);
        (await AssignmentStatusAsync(world.AdviserB, world.AccountB)).ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Disable_OnlyInTheBook_Adviser_Returns204AndRemovesTheAssignment()
    {
        var world = await AdviserBookWorld.CreateAsync("book-disable-own");
        await world.AssignAsync(world.AdviserA, world.AccountA);

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/portfolios/{world.PortfolioId}/disable", world.AdviserA.AccessToken, new
        {
            rowVersion = await world.RowVersionAsync()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await world.GetPortfolioAsync()).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await AssignmentStatusAsync(world.AdviserA, world.AccountA)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Disable_SharedPortfolio_TenantAdmin_Returns204AndRemovesEveryAssignment()
    {
        var world = await AdviserBookWorld.CreateAsync("book-disable-admin");
        await world.ShareAsync();

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/portfolios/{world.PortfolioId}/disable", world.TenantAdmin.AccessToken, new
        {
            rowVersion = await world.RowVersionAsync()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await world.GetPortfolioAsync()).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await AssignmentStatusAsync(world.AdviserA, world.AccountA)).ShouldBe(HttpStatusCode.NotFound);
        (await AssignmentStatusAsync(world.AdviserB, world.AccountB)).ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<HttpStatusCode> AssignmentStatusAsync(TokenResponse tokens, Guid accountId) =>
        (await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/portfolio", tokens.AccessToken)).StatusCode;
}
