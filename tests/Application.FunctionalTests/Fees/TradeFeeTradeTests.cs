using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests.Fees;

/// <summary>
/// Trade fees R7, R14–R18: the <c>Fee</c> cash leg on <c>Buy</c> / <c>Sell</c> from <c>POST /transactions</c>.
/// Schedules apply to trades on Custodial accounts (external-caller-numbers R22), which the server books now.
/// </summary>
[Parallelizable]
public class TradeFeeTradeTests : TestBase
{

    [Test]
    public async Task Buy_WithSchedule_PostsAFeeLegAndCapitalisesTheFee()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);

        var buy = await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m);

        buy.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashAmountsAsync(admin.AccessToken, await ReadIdAsync(buy))).ShouldBe([-1250m, -10m], ignoreOrder: true);
        (await CashBalanceAsync(admin.AccessToken, accountId)).ShouldBe(100000m - 1260m);
        (await HoldingCostAsync(admin.AccessToken, accountId, instrumentId)).ShouldBe(1260m);
        (await FeeDetailsAsync()).ShouldBe([(0, 1250m, 10m, "USD")]);
    }

    [Test]
    public async Task Sell_WithSchedule_FeeOnlyReducesCash()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        (await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);

        var sell = await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Sell", instrumentId, 50m);

        sell.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashAmountsAsync(admin.AccessToken, await ReadIdAsync(sell))).ShouldBe([1250m, -10m], ignoreOrder: true);
        (await CashBalanceAsync(admin.AccessToken, accountId)).ShouldBe(100000m - 2500m + 1250m - 10m);
        (await HoldingCostAsync(admin.AccessToken, accountId, instrumentId)).ShouldBe(1250m);
    }

    [Test]
    public async Task TheScheduleInEffectOnTheServersDateApplies()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        var beforeAny = await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 1m);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today.AddDays(-9));
        var underTheFirst = await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 1m);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today, minimum: 20.00m, new FeeBand(0m, 0.5m));
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today.AddDays(1), minimum: 30.00m, new FeeBand(0m, 0.5m));

        var underTheSecond = await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 1m);

        (await CashAmountsAsync(admin.AccessToken, await ReadIdAsync(beforeAny))).ShouldBe([-25m]);
        (await CashAmountsAsync(admin.AccessToken, await ReadIdAsync(underTheFirst))).ShouldBe([-25m, -10m], ignoreOrder: true);
        (await CashAmountsAsync(admin.AccessToken, await ReadIdAsync(underTheSecond))).ShouldBe([-25m, -20m], ignoreOrder: true);
    }

    [Test]
    public async Task CrossCurrencySell_ConvertsTheFeeWithThePrincipalRate()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var accountId = await FeeHttp.OpenCustodialAsync(admin.AccessToken, customer.PublicId, "NZD");
        var instrumentId = await FeeHttp.CreateInstrumentAsync(admin.AccessToken, "VTI", "USD");
        await FeeHttp.SetPriceAsync(instrumentId, 25.00m, "USD");
        await SetRateAsync("USD", "NZD", 1.6543m);
        (await FeeHttp.FundCustodialAsync(admin.AccessToken, accountId, 10000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 100m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);

        var sell = await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Sell", instrumentId, 50m);

        sell.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashAmountsAsync(admin.AccessToken, await ReadIdAsync(sell))).ShouldBe([2067.875m, -16.543m], ignoreOrder: true);
        (await FeeDetailsAsync()).ShouldBe([(0, 1250m, 10m, "USD")]);
    }

    [Test]
    public async Task FeeFieldOnTheCreateBody_Returns400()
    {
        var (_, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);

        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions", admin.AccessToken, new
        {
            accountId,
            type = "Buy",
            fee = 5m,
            security = new[] { new { instrumentId, quantity = 1m } }
        }, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).ShouldContainKey("Fee");
    }

    [Test]
    public async Task Buy_CashCoversThePrincipalButNotTheFee_Returns400AndWritesNothing()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId, cash: 1255m);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);

        var buy = await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m);

        buy.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CashBalanceAsync(admin.AccessToken, accountId)).ShouldBe(1255m);
        (await FeeDetailsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ATradeUsesOnlyItsOwnTenantsSchedules()
    {
        var (northTenant, _, _, _) = await AccountHttp.SignInTenantAdmin();
        var (_, _, southCustomer, south) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        await FeeHttp.AddScheduleAsync(northTenant.Id, "USD", FeeHttp.Today);
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(south.AccessToken, southCustomer.PublicId);

        var buy = await FeeHttp.PostTradeAsync(south.AccessToken, accountId, "Buy", instrumentId, 50m);

        buy.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashAmountsAsync(south.AccessToken, await ReadIdAsync(buy))).ShouldBe([-1250m]);
    }

    [Test]
    public async Task Reverse_RefundsTheFeeInFull_AndCopiesTheFeeDetail()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        var scheduleId = await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);
        var buyId = await ReadIdAsync(
            await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m));

        var reverse = await TransactionHttp.Send(HttpMethod.Post, $"/transactions/{buyId}/reverse", admin.AccessToken, null, Guid.NewGuid());

        var reversalId = await ReadIdAsync(reverse);
        (await CashAmountsAsync(admin.AccessToken, reversalId)).ShouldBe([1250m, 10m], ignoreOrder: true);
        (await CashBalanceAsync(admin.AccessToken, accountId)).ShouldBe(100000m);
        (await FeeDetailsAsync()).ShouldBe([(0, 1250m, 10m, "USD"), (0, 1250m, 10m, "USD")]);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedule = await db.TradeFeeSchedules.IgnoreQueryFilters().SingleAsync(row => row.PublicId == scheduleId);
        (await db.TransactionFeeDetails.IgnoreQueryFilters().Select(row => row.TradeFeeScheduleId).ToListAsync())
            .ShouldAllBe(id => id == schedule.Id);
    }

    internal static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    internal static async Task<decimal[]> CashAmountsAsync(string accessToken, Guid transactionId)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{transactionId}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return TransactionJson.CashLegs(json.RootElement).Select(leg => leg.GetProperty("amount").GetDecimal()).ToArray();
    }

    internal static async Task<decimal> CashBalanceAsync(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("cashBalance").GetDecimal();
    }

    internal static async Task<decimal> HoldingCostAsync(string accessToken, Guid accountId, Guid instrumentId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("instrumentId").GetGuid() == instrumentId)
            .GetProperty("cost").GetDecimal();
    }

    /// <summary>Every fee detail row: kind, basis, fee and currency.</summary>
    internal static async Task<List<(int Kind, decimal Basis, decimal Fee, string Currency)>> FeeDetailsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.TransactionFeeDetails.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        return rows.Select(row => ((int)row.Kind, row.BasisAmount, row.FeeAmount, row.Currency)).ToList();
    }

    internal static async Task SetRateAsync(string from, string to, decimal rate)
    {
        await using var scope = FunctionalTestSetup.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.FxRates.Add(new FxRate
        {
            BaseCurrency = from,
            QuoteCurrency = to,
            Rate = rate,
            AsOfDate = new DateOnly(2026, 10, 11),
            Source = "Frankfurter",
            FetchedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
