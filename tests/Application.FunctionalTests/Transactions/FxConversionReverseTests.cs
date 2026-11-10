using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>FX conversions R17: reverse at the original amounts and rate, copies the FX detail, all or nothing.</summary>
public class FxConversionReverseTests : TestBase
{
    [Test]
    public async Task Reverse_RestoresBothAccountsAndCopiesTheFxDetail()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(1000m);
        var conversionId = await ConvertAsync(world, 1000.00m);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(0m);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.UsdAccountId)).ShouldBe(591.20m);

        var reversalId = await LegAccountsFixture.ReadIdAsync(
            await LegAccountsFixture.ReverseAsync(world.Legs.TenantAdmin.AccessToken, conversionId));

        (await LegAccountsFixture.CashLegsAsync(reversalId)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.UsdAccountId, -591.20m),
            (world.NzdAccountId, 1000.00m)
        ]);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(1000m);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.UsdAccountId)).ShouldBe(0m);
        (await FxConversionTests.FxDetailAsync(reversalId))
            .ShouldBe(("NZD", "USD", FxConversionTests.NzdUsd, FxConversionTests.RateAsOf, "Frankfurter"));

        var get = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{conversionId}", world.Legs.TenantAdmin.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("reversedByTransactionId").GetGuid().ShouldBe(reversalId);
    }

    [Test]
    public async Task Reverse_UsesTheOriginalAmountsAfterTheRateChanged()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(1000m);
        var conversionId = await ConvertAsync(world, 1000.00m);
        await FxConversionTests.SetRateAsync("NZD", "USD", 0.70000000m, DateOnly.FromDateTime(DateTime.UtcNow));
        await FxConversionTests.SetRateAsync("NZD", "USD", 0.70000000m, FxConversionTests.RateAsOf);

        var reversalId = await LegAccountsFixture.ReadIdAsync(
            await LegAccountsFixture.ReverseAsync(world.Legs.TenantAdmin.AccessToken, conversionId));

        (await LegAccountsFixture.CashLegsAsync(reversalId)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.UsdAccountId, -591.20m),
            (world.NzdAccountId, 1000.00m)
        ]);
        (await FxConversionTests.FxDetailAsync(reversalId))!.Value.Rate.ShouldBe(FxConversionTests.NzdUsd);
    }

    [Test]
    public async Task Reverse_WithNoCurrentRate_StillSucceeds()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var conversionId = await ConvertAsync(world, 10m);
        await DeleteRatesAsync();

        (await LegAccountsFixture.ReverseAsync(world.Legs.TenantAdmin.AccessToken, conversionId))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Reverse_AfterTheToAccountSpentTheCash_Returns400AndWritesNothing()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(1000m);
        var conversionId = await ConvertAsync(world, 1000.00m);
        await LegAccountsFixture.InsertCashTransactionAsync(
            world.Legs.Tenant.Id, TransactionType.TransferOut, (world.UsdAccountId, -0.01m));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);
        var detailsBefore = await FxConversionTests.CountFxDetailsAsync();

        (await LegAccountsFixture.ReverseAsync(world.Legs.TenantAdmin.AccessToken, conversionId))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await FxConversionTests.CountFxDetailsAsync()).ShouldBe(detailsBefore);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(0m);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.UsdAccountId)).ShouldBe(591.19m);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Reverse_WhenEitherAccountIsClosed_Returns400(bool closeFrom)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var conversionId = await ConvertAsync(world, 10m);
        await LegAccountsFixture.CloseAccountDirectlyAsync(closeFrom ? world.NzdAccountId : world.UsdAccountId);

        (await LegAccountsFixture.ReverseAsync(world.Legs.TenantAdmin.AccessToken, conversionId))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Reverse_UnassignedAdviser_Returns404()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var conversionId = await ConvertAsync(world, 10m);

        (await LegAccountsFixture.ReverseAsync(world.Legs.Bob.AccessToken, conversionId))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> ConvertAsync(FxWorld world, decimal sellAmount) =>
        await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            world.Legs.TenantAdmin.AccessToken,
            FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, sellAmount)));

    private static async Task DeleteRatesAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyWealthV2.Infrastructure.Data.ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [FxRates]");
    }
}
