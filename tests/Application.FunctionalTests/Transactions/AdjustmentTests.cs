using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Adjustments R1, R2, R4, R6, R9–R11, R13, R14, R16: who may adjust, and what an adjustment books.</summary>
[Parallelizable]
public class AdjustmentTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TransactionHttp.Send(
            HttpMethod.Post,
            AdjustmentWorld.Path,
            accessToken: null,
            AdjustmentWorld.Body(Guid.NewGuid(), amount: 5m),
            Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task AdviserCustomerAndSystemAdmin_Return403AndWriteNothing()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();
        var body = AdjustmentWorld.Body(world.AccountId, amount: 5m);

        foreach (var token in new[] { world.AdviserToken, world.CustomerToken, system.AccessToken })
        {
            (await AdjustmentWorld.PostAsync(token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task Adviser_UnknownAccount_Returns403Not404()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var response = await AdjustmentWorld.PostAsync(
            world.AdviserToken, AdjustmentWorld.Body(Guid.NewGuid(), amount: 5m));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_CashIn_Returns201WithLocation_AndBooksTheCash()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow;

        var response = await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 25m));

        var id = await ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"/transactions/{id}");
        (await world.CashAsync()).ShouldBe(25m);
        using var item = await GetTransactionAsync(world.Admin, id);
        var root = item.RootElement;
        root.GetProperty("type").GetString().ShouldBe("Adjustment");
        root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("Manual");
        root.GetProperty("source").GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("memo").GetString().ShouldBe("Correction after the broker statement");
        var bookedAt = root.GetProperty("bookedAt").GetDateTimeOffset();
        bookedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        bookedAt.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(1));
        var cash = root.GetProperty("cash").EnumerateArray().ShouldHaveSingleItem();
        cash.GetProperty("amount").GetDecimal().ShouldBe(25m);
        cash.GetProperty("currency").GetString().ShouldBe("USD");
        cash.GetProperty("role").GetString().ShouldBe("Principal");
        root.GetProperty("security").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task TenantAdmin_CashOut_LowersTheCashBalance()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);

        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: -40m)));

        (await world.CashAsync()).ShouldBe(60m);
    }

    [Test]
    public async Task TenantAdmin_NewHoldingFromZero_CreatesTheProjection()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var id = await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId,
            security: AdjustmentWorld.Holding(world.InstrumentId, 10m, 250m),
            reason: "ReconciliationBreak")));

        (await world.HoldingAsync()).ShouldBe((10m, 250m));
        (await world.CashAsync()).ShouldBe(0m);
        using var item = await GetTransactionAsync(world.Admin, id);
        item.RootElement.GetProperty("cash").GetArrayLength().ShouldBe(0);
        var leg = item.RootElement.GetProperty("security").EnumerateArray().ShouldHaveSingleItem();
        leg.GetProperty("instrumentId").GetGuid().ShouldBe(world.InstrumentId);
        leg.GetProperty("quantity").GetDecimal().ShouldBe(10m);
        leg.GetProperty("cost").GetDecimal().ShouldBe(250m);
        leg.GetProperty("costCurrency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task TenantAdmin_SplitByHand_ChangesOnlyTheQuantity()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(1000m);
        await world.BuyAsync(10m);
        var before = await world.HoldingAsync();

        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId,
            security: AdjustmentWorld.Holding(world.InstrumentId, 10m, 0m),
            reason: "CorporateAction")));

        (await world.HoldingAsync()).ShouldBe((20m, before!.Value.Cost));
    }

    [Test]
    public async Task TenantAdmin_CostOnlyChange_ChangesOnlyTheCost()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(1000m);
        await world.BuyAsync(10m);
        var before = await world.HoldingAsync();

        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 0m, 0.60m))));

        (await world.HoldingAsync()).ShouldBe((10m, before!.Value.Cost + 0.60m));
    }

    [Test]
    public async Task TenantAdmin_CashAndHoldingTogether_BooksBothLegsInOneTransaction()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(100m);
        await world.BuyAsync(2m);
        var cashBefore = await world.CashAsync();
        var holdingBefore = await world.HoldingAsync();
        var countBefore = await TransactionCountAsync(world.AccountId);

        var id = await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, amount: -0.60m, security: AdjustmentWorld.Holding(world.InstrumentId, 0m, 0.60m))));

        (await world.CashAsync()).ShouldBe(cashBefore - 0.60m);
        (await world.HoldingAsync()).ShouldBe((2m, holdingBefore!.Value.Cost + 0.60m));
        (await TransactionCountAsync(world.AccountId)).ShouldBe(countBefore + 1);
        using var item = await GetTransactionAsync(world.Admin, id);
        item.RootElement.GetProperty("cash").GetArrayLength().ShouldBe(1);
        item.RootElement.GetProperty("security").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task TenantAdmin_HoldingToZero_RemovesTheProjection()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 10m, 250m))));

        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, -10m, -250m))));

        (await world.HoldingAsync()).ShouldBeNull();
    }

    [Test]
    public async Task DisabledInstrument_WithAnExistingHolding_Returns201()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(1000m);
        await world.BuyAsync(10m);
        await AdjustmentWorld.DisableInstrumentAsync(world.InstrumentId);

        var response = await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, -1m, -25m)));

        await ReadIdAsync(response);
        (await world.HoldingAsync())!.Value.Quantity.ShouldBe(9m);
    }

    [Test]
    public async Task DisabledInstrument_WithoutAHolding_Returns400AndWritesNothing()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await AdjustmentWorld.DisableInstrumentAsync(world.InstrumentId);

        var response = await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 10m, 250m)));

        (await ErrorsAsync(response)).ShouldContainKey("Security");
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task AccountWithAnActivePortfolio_Returns201_KeepsTheAssignment_AndEnqueuesNoRebalance()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", world.Admin, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = world.InstrumentId, targetWeight = 100m } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var portfolioId = createdJson.RootElement.GetProperty("id").GetGuid();
        await world.FundAsync(1000m);
        var assign = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{world.AccountId}/portfolio", world.Admin, new { portfolioId });
        assign.StatusCode.ShouldBe(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();
        var enqueuedBefore = scheduler.Enqueued.Count;

        var response = await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, amount: -1m, security: AdjustmentWorld.Holding(world.InstrumentId, 1m, 25m)));

        await ReadIdAsync(response);
        scheduler.Enqueued.Count.ShouldBe(enqueuedBefore);
        var assignment = await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{world.AccountId}/portfolio", world.Admin);
        var content = await assignment.Content.ReadAsStringAsync();
        assignment.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var assignmentJson = JsonDocument.Parse(content);
        assignmentJson.RootElement.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Test]
    public async Task CreateTransactionRoute_RefusesTheAdjustmentType()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var response = await PostAsync(world.Admin, Cash(world.AccountId, "Adjustment", 5m));

        (await ErrorsAsync(response)).ShouldContainKey("Type");
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }
}
