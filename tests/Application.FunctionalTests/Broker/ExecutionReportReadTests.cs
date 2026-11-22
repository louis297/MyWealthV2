using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R30: <c>GET /execution-reports</c>.</summary>
[Parallelizable]
public class ExecutionReportReadTests : TestBase
{
    private const string Path = "/execution-reports";
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";

    private static readonly DateTimeOffset At = new(2026, 11, 23, 2, 13, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Get, Path, accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
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

    [Test]
    public async Task SystemAdmin_ListsNewestFirst_WithEveryField()
    {
        await InsertAsync();
        var system = await TenantHttp.SignInSystemAdmin();

        var root = await ReadAsync(system.AccessToken);

        (root.GetProperty("page").GetInt32(), root.GetProperty("pageSize").GetInt32(), root.GetProperty("totalCount").GetInt32())
            .ShouldBe((1, 20, 4));
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("outsideId").GetString()).ShouldBe(["w-X4", "w-X3", "w-X2", "w-X1"]);

        var fill = items[3];
        fill.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "connection", "outsideId", "kind", "clientOrderId", "brokerOrderId", "quantity", "price", "tradeDate",
            "settlementDate", "contractNote", "fillOutsideId", "settledDate", "reason", "receivedAt", "status", "skipReason",
            "processedAt"
        ], ignoreOrder: true);
        fill.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        fill.GetProperty("connection").GetString().ShouldBe("simulator");
        fill.GetProperty("kind").GetString().ShouldBe("Fill");
        fill.GetProperty("clientOrderId").GetString().ShouldBe(OrderId);
        fill.GetProperty("brokerOrderId").GetString().ShouldBe("w-O1");
        fill.GetProperty("quantity").GetDecimal().ShouldBe(4m);
        fill.GetProperty("price").GetDecimal().ShouldBe(100.12m);
        fill.GetProperty("tradeDate").GetString().ShouldBe("2026-11-23");
        fill.GetProperty("settlementDate").GetString().ShouldBe("2026-11-25");
        fill.GetProperty("contractNote").GetString().ShouldBe("CN-1");
        fill.GetProperty("fillOutsideId").ValueKind.ShouldBe(JsonValueKind.Null);
        fill.GetProperty("settledDate").ValueKind.ShouldBe(JsonValueKind.Null);
        fill.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        fill.GetProperty("receivedAt").GetDateTimeOffset().ShouldBe(At);
        fill.GetProperty("status").GetString().ShouldBe("Processed");
        fill.GetProperty("skipReason").ValueKind.ShouldBe(JsonValueKind.Null);
        fill.GetProperty("processedAt").GetDateTimeOffset().ShouldBe(At.AddMinutes(1));

        var cancelled = items[2];
        (cancelled.GetProperty("kind").GetString(), cancelled.GetProperty("reason").GetString(), cancelled.GetProperty("status").GetString())
            .ShouldBe(("Cancelled", "Requested", "Received"));
        cancelled.GetProperty("processedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        var settled = items[1];
        (settled.GetProperty("kind").GetString(), settled.GetProperty("fillOutsideId").GetString(),
                settled.GetProperty("settledDate").GetString(), settled.GetProperty("status").GetString(),
                settled.GetProperty("skipReason").GetString())
            .ShouldBe(("Settled", "w-X9", "2026-11-25", "Skipped", "UnknownFill"));

        var unknown = items[0];
        unknown.GetProperty("kind").ValueKind.ShouldBe(JsonValueKind.Null);
        (unknown.GetProperty("connection").GetString(), unknown.GetProperty("status").GetString(),
                unknown.GetProperty("skipReason").GetString())
            .ShouldBe(("other-broker", "Skipped", "UnsupportedKind"));
    }

    [Test]
    public async Task Filters_ByConnection_AndByOneOrMoreStatuses_InAnyCase()
    {
        await InsertAsync();
        var system = await TenantHttp.SignInSystemAdmin();

        (await OutsideIdsAsync(system.AccessToken, "?connection=simulator")).ShouldBe(["w-X3", "w-X2", "w-X1"]);
        (await OutsideIdsAsync(system.AccessToken, "?connection=other-broker")).ShouldBe(["w-X4"]);
        (await OutsideIdsAsync(system.AccessToken, "?connection=nobody")).ShouldBeEmpty();
        (await OutsideIdsAsync(system.AccessToken, "?status=Skipped")).ShouldBe(["w-X4", "w-X3"]);
        (await OutsideIdsAsync(system.AccessToken, "?status=received,PROCESSED")).ShouldBe(["w-X2", "w-X1"]);
        (await OutsideIdsAsync(system.AccessToken, "?connection=simulator&status=Skipped")).ShouldBe(["w-X3"]);
    }

    [Test]
    public async Task Pages_AreCutNewestFirst()
    {
        await InsertAsync();
        var system = await TenantHttp.SignInSystemAdmin();

        var root = await ReadAsync(system.AccessToken, "?page=2&pageSize=3");

        (root.GetProperty("page").GetInt32(), root.GetProperty("pageSize").GetInt32(), root.GetProperty("totalCount").GetInt32())
            .ShouldBe((2, 3, 4));
        root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("outsideId").GetString()).ShouldBe(["w-X1"]);
    }

    [TestCase("?status=Broken")]
    [TestCase("?status=Received,Broken")]
    public async Task AnUnknownStatus_Returns400AtStatus(string query)
    {
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Get, Path + query, system.AccessToken);

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errors").GetProperty("Status").EnumerateArray().Single().GetString()
            .ShouldBe("Status must be one or more of Received, Processed, Skipped, comma-separated.");
    }

    /// <summary>Four reports, received a minute apart: a processed fill, a cancellation still to process, a
    /// <c>Settled</c> report skipped for an unknown fill, and a report of an unknown kind on another connection.</summary>
    private static async Task InsertAsync()
    {
        var fill = ExecutionReport.Receive(
            "simulator", "w-X1", ExecutionReportKind.Fill, OrderId, "w-O1", 4m, 100.12m, Day, Day.AddDays(2), "CN-1", null, null,
            null, At);
        fill.MarkProcessed(At.AddMinutes(1));
        var cancelled = ExecutionReport.Receive(
            "simulator", "w-X2", ExecutionReportKind.Cancelled, OrderId, "w-O1", null, null, null, null, null, null, null,
            "Requested", At.AddMinutes(1));
        var settled = ExecutionReport.Receive(
            "simulator", "w-X3", ExecutionReportKind.Settled, OrderId, "w-O1", null, null, null, null, null, "w-X9", Day.AddDays(2),
            null, At.AddMinutes(2));
        settled.Skip(ExecutionReportSkipReason.UnknownFill, At.AddMinutes(3));
        var unknown = ExecutionReport.Receive(
            "other-broker", "w-X4", null, OrderId, null, null, null, null, null, null, null, null, null, At.AddMinutes(3));
        unknown.Skip(ExecutionReportSkipReason.UnsupportedKind, At.AddMinutes(4));

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ExecutionReports.AddRange(fill, cancelled, settled, unknown);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadAsync(string accessToken, string query = "")
    {
        var response = await TenantHttp.Send(HttpMethod.Get, Path + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    private static async Task<List<string?>> OutsideIdsAsync(string accessToken, string query) =>
        (await ReadAsync(accessToken, query)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("outsideId").GetString())
            .ToList();
}
