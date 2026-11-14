using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWealthV2.Application.FunctionalTests.Infrastructure;
using RouteHandlerOptions = Microsoft.AspNetCore.Routing.RouteHandlerOptions;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

// fixes/utc-instants.md B2: a request instant must carry Z or an offset; the server converts it to UTC.
public class BookedAtOffsetTests : TestBase
{
    private static readonly string[] Routes =
        ["/transactions", "/transactions/transfer", "/transactions/fx", "/transactions/fx/quote"];

    private static IEnumerable<TestCaseData> Rejected() =>
        from route in Routes
        from value in new[] { "2026-11-14T16:13", "yesterday" }
        select new TestCaseData(route, value);

    [TestCaseSource(nameof(Rejected))]
    public async Task BookedAtWithoutAnOffset_Returns400(string route, string bookedAt)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(1000m);
        var savings = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned savings");
        var body = BodyFor(route, world, savings);
        body["bookedAt"] = bookedAt;
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        var response = await TransactionHttp.Send(
            HttpMethod.Post, route, world.Legs.TenantAdmin.AccessToken, body,
            route.EndsWith("/quote", StringComparison.Ordinal) ? null : Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldNotBeNullOrEmpty();
        using var json = JsonDocument.Parse(content);
        var errors = json.RootElement.GetProperty("errors").EnumerateObject().ToList();
        var field = errors.SingleOrDefault(
            property => string.Equals(property.Name, "bookedAt", StringComparison.OrdinalIgnoreCase));
        field.Value.ValueKind.ShouldBe(JsonValueKind.Array);
        field.Value.EnumerateArray().Single().GetString()
            .ShouldBe("Send an ISO 8601 date and time with Z or an offset, for example 2026-11-14T03:13:00Z.");
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task SameInstantWithAnotherOffset_ReplaysTheSameTransaction()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var key = Guid.NewGuid();

        var first = await PostTransferInAsync(world, "2026-11-14T09:00:00+13:00", key);
        var second = await PostTransferInAsync(world, "2026-11-13T20:00:00Z", key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadIdAsync(second)).ShouldBe(await ReadIdAsync(first));
    }

    [Test]
    public void Production_ThrowsOnBadRequest()
    {
        using var factory = new WebApiFactory(FunctionalTestSetup.ConnectionString, environment: "Production");

        factory.Services.GetRequiredService<IOptions<RouteHandlerOptions>>().Value.ThrowOnBadRequest.ShouldBeTrue();
    }

    private static Dictionary<string, object?> BodyFor(string route, FxWorld world, Guid savings) => route switch
    {
        "/transactions" => new()
        {
            ["accountId"] = world.NzdAccountId,
            ["type"] = "TransferIn",
            ["amount"] = 10m,
            ["bookedAt"] = FxConversionTests.BookedAt
        },
        "/transactions/transfer" => InternalTransferTests.Body(world.NzdAccountId, savings, 10m),
        "/transactions/fx" => FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m),
        _ => new()
        {
            ["fromAccountId"] = world.NzdAccountId,
            ["toAccountId"] = world.UsdAccountId,
            ["sellAmount"] = 10m,
            ["bookedAt"] = FxConversionTests.BookedAt
        }
    };

    private static Task<HttpResponseMessage> PostTransferInAsync(LegAccountsWorld world, string bookedAt, Guid key) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", world.TenantAdmin.AccessToken, new Dictionary<string, object?>
        {
            ["accountId"] = world.NedAccountId,
            ["type"] = "TransferIn",
            ["amount"] = 10m,
            ["bookedAt"] = bookedAt
        }, key);

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
