using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>fixes/ledger-write-guards.md B3: a bookedAt later than the server clock plus 5 minutes is refused.</summary>
[Parallelizable]
public class FutureBookedAtTests : TestBase
{
    private static readonly string[] CreateRoutes = ["/transactions", "/transactions/transfer", "/transactions/fx"];
    private const string QuoteRoute = "/transactions/fx/quote";

    [TestCaseSource(nameof(CreateRoutes))]
    public async Task Create_BookedAtOneHourAhead_Returns400AndWritesNothing(string route)
    {
        var (world, savings) = await WorldAsync();
        var body = BodyFor(route, world, savings);
        body["bookedAt"] = Instant(DateTimeOffset.UtcNow.AddHours(1));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);
        var key = Guid.NewGuid();

        var response = await TransactionHttp.Send(HttpMethod.Post, route, world.Legs.TenantAdmin.AccessToken, body, key);

        await AssertBookedAtErrorAsync(response);
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await IdempotencyRecordExistsAsync(world.Legs.Tenant.Id, key)).ShouldBeFalse();
    }

    [TestCaseSource(nameof(CreateRoutes))]
    public async Task Create_BookedAtNow_Returns201(string route)
    {
        var (world, savings) = await WorldAsync();
        var body = BodyFor(route, world, savings);
        body["bookedAt"] = Instant(DateTimeOffset.UtcNow);

        var response = await TransactionHttp.Send(
            HttpMethod.Post, route, world.Legs.TenantAdmin.AccessToken, body, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Quote_BookedAtOneHourAhead_Returns400()
    {
        var (world, savings) = await WorldAsync();
        var body = BodyFor(QuoteRoute, world, savings);
        body["bookedAt"] = Instant(DateTimeOffset.UtcNow.AddHours(1));

        await AssertBookedAtErrorAsync(
            await TransactionHttp.Send(HttpMethod.Post, QuoteRoute, world.Legs.TenantAdmin.AccessToken, body));
    }

    /// <summary>External caller numbers R19: a quote between External Accounts is refused once bookedAt is resolved.</summary>
    [Test]
    public async Task Quote_WithoutBookedAt_BetweenExternalAccounts_Returns400OnFromAccountId()
    {
        var (world, savings) = await WorldAsync();
        var body = BodyFor(QuoteRoute, world, savings);
        body.Remove("bookedAt");

        var response = await TransactionHttp.Send(HttpMethod.Post, QuoteRoute, world.Legs.TenantAdmin.AccessToken, body);

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => property.Name.ToLowerInvariant()).ShouldBe(["fromaccountid"]);
    }

    /// <summary>The FX world, funded, with a rate as of today so that a conversion booked now is not stale.</summary>
    private static async Task<(FxWorld World, Guid Savings)> WorldAsync()
    {
        var world = await FxWorld.CreateExternalAsync();
        await world.FundNzdAsync(1000m);
        await FxConversionTests.SetRateAsync(
            "NZD", "USD", FxConversionTests.NzdUsd, DateOnly.FromDateTime(DateTime.UtcNow));
        var savings = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned savings");
        return (world, savings);
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
        "/transactions/fx" => FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 10m, 5.91m),
        _ => new()
        {
            ["fromAccountId"] = world.NzdAccountId,
            ["toAccountId"] = world.UsdAccountId,
            ["sellAmount"] = 10m,
            ["bookedAt"] = FxConversionTests.BookedAt
        }
    };

    private static string Instant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static async Task AssertBookedAtErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        var field = json.RootElement.GetProperty("errors").EnumerateObject()
            .SingleOrDefault(property => string.Equals(property.Name, "bookedAt", StringComparison.OrdinalIgnoreCase));
        field.Value.ValueKind.ShouldBe(JsonValueKind.Array, content);
        field.Value.EnumerateArray().Select(value => value.GetString())
            .ShouldContain("bookedAt cannot be in the future.");
    }

    private static async Task<bool> IdempotencyRecordExistsAsync(int tenantId, Guid key)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.IdempotencyRecords.AnyAsync(record => record.TenantId == tenantId && record.Key == key);
    }
}
