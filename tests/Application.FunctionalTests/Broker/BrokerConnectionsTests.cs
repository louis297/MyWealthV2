using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R4: <c>GET /broker-connections</c>.</summary>
[Parallelizable]
public class BrokerConnectionsTests : TestBase
{
    private const string Path = "/broker-connections";

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Get, Path, accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_GetsNameAndAdapter_OnPageOne_AndNoReportsUntilTheFirstRead()
    {
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Get, Path, system.AccessToken);

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        root.GetProperty("page").GetInt32().ShouldBe(1);
        root.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = root.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("name").GetString().ShouldBe("simulator");
        item.GetProperty("adapter").GetString().ShouldBe("Simulator");
        item.GetProperty("reports").ValueKind.ShouldBe(JsonValueKind.Null);
        item.EnumerateObject().Select(property => property.Name).ShouldBe(["name", "adapter", "reports"], ignoreOrder: true);
    }

    [Test]
    public async Task SystemAdmin_SeesTheStateOfTheFeed_WithTheFailureAsAName()
    {
        var succeeded = new DateTimeOffset(2026, 11, 23, 2, 13, 0, TimeSpan.Zero);
        var feed = BrokerFeed.Start("simulator");
        feed.Advance("w:3", succeeded);
        feed.RecordFailure(BrokerFailure.InvalidItem, succeeded.AddMinutes(1));
        var other = BrokerFeed.Start("not-configured");
        other.Advance("x:1", succeeded);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.BrokerFeeds.AddRange(feed, other);
            await db.SaveChangesAsync();
        }

        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Get, Path, system.AccessToken);

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var reports = json.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("reports");
        reports.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["lastSucceededAt", "lastFailedAt", "lastFailure"], ignoreOrder: true);
        reports.GetProperty("lastSucceededAt").GetDateTimeOffset().ShouldBe(succeeded);
        reports.GetProperty("lastFailedAt").GetDateTimeOffset().ShouldBe(succeeded.AddMinutes(1));
        reports.GetProperty("lastFailure").GetString().ShouldBe("InvalidItem");
        content.ShouldNotContain("w:3");
    }

    [Test]
    public async Task TenantAdminAdviserAndCustomer_Return403()
    {
        var (_, _, customer, tenantAdmin) = await AccountHttp.SignInTenantAdmin(
            "Broker Firm", "broker-firm", "tess@broker.example",
            adviserEmail: "ann@broker.example", customerEmail: "ned@broker.example");
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@broker.example", "Password1!", "broker-firm");
        var customerToken = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var token in new[] { tenantAdmin.AccessToken, adviser.AccessToken, customerToken.AccessToken })
        {
            (await TenantHttp.Send(HttpMethod.Get, Path, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }
}
