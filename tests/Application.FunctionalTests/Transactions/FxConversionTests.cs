using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>FX conversions R1, R3–R5, R7–R11, R13–R15, R19: POST /transactions/fx create, idempotency and validation.</summary>
public class FxConversionTests : TestBase
{
    internal const string FxPath = "/transactions/fx";
    internal const string BookedAt = "2026-11-10T09:00:00Z";
    internal const decimal NzdUsd = 0.59120000m;
    internal static readonly DateOnly RateAsOf = new(2026, 11, 9);

    [Test]
    public async Task NoBearer_Returns401()
    {
        var response = await TransactionHttp.Send(
            HttpMethod.Post, FxPath, accessToken: null, SellBody(Guid.NewGuid(), Guid.NewGuid(), 10m), Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var world = await FxWorld.CreateAsync();
        var nedTokens = await TestApp.IssueAccessTokenAsync(world.Legs.Ned);

        (await PostAsync(nedTokens.AccessToken, SellBody(world.NzdAccountId, world.UsdAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_FixedSell_BooksBothLegsAndStoresTheRate()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);

        var response = await PostAsync(
            world.Legs.TenantAdmin.AccessToken,
            SellBody(world.NzdAccountId, world.UsdAccountId, 1000.00m, memo: "USD for US ETF"));

        var id = await LegAccountsFixture.ReadIdAsync(response);
        response.Headers.Location?.OriginalString.ShouldBe($"/transactions/{id}");
        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NzdAccountId, -1000.00m),
            (world.UsdAccountId, 591.20m)
        ]);
        (await CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(1000m);
        (await CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.UsdAccountId)).ShouldBe(591.20m);
        (await FxDetailAsync(id)).ShouldBe(("NZD", "USD", NzdUsd, RateAsOf, "Frankfurter"));

        var get = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", world.Legs.TenantAdmin.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("type").GetString().ShouldBe("FxConversion");
        json.RootElement.GetProperty("memo").GetString().ShouldBe("USD for US ETF");
    }

    [Test]
    public async Task AssignedAdviser_FixedBuy_ComputesTheSellAmount()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);

        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(
            world.Legs.Ann.AccessToken, BuyBody(world.NzdAccountId, world.UsdAccountId, 500.00m)));

        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NzdAccountId, -845.74m),
            (world.UsdAccountId, 500.00m)
        ]);
        (await FxDetailAsync(id)).ShouldBe(("NZD", "USD", NzdUsd, RateAsOf, "Frankfurter"));
    }

    [Test]
    public async Task UsesTheLatestRateOnOrBeforeTheBookedDate()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);
        await SetRateAsync("NZD", "USD", 0.58000000m, new DateOnly(2026, 11, 8));
        await SetRateAsync("NZD", "USD", 0.60000000m, new DateOnly(2026, 11, 11));

        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(
            world.Legs.TenantAdmin.AccessToken, SellBody(world.NzdAccountId, world.UsdAccountId, 100m)));

        (await FxDetailAsync(id)).ShouldBe(("NZD", "USD", NzdUsd, RateAsOf, "Frankfurter"));
    }

    [Test]
    public async Task TwoCustomers_Returns400AndWritesNothing()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var noraUsd = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Nora.PublicId, "Nora USD", "USD");

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, noraUsd, 10m));
    }

    [Test]
    public async Task TwoCustomersOfTheSameAdviser_Returns400AndWritesNothing()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var nell = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Nell North", "nell@north.example", "Password1!", world.Legs.Tenant.Id, world.Legs.Ned.AdviserId);
        var nellUsd = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, nell.PublicId, "Nell USD", "USD");

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, nellUsd, 10m), world.Legs.Ann.AccessToken);
    }

    [Test]
    public async Task SameAccount_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, world.NzdAccountId, 10m));
    }

    [Test]
    public async Task SameCurrency_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var savings = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned savings");

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, savings, 10m));
    }

    [Test]
    public async Task BothAmounts_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body["buyAmount"] = 5m;

        await ShouldRejectAsync(world, body);
    }

    [Test]
    public async Task NeitherAmount_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body.Remove("sellAmount");

        await ShouldRejectAsync(world, body);
    }

    [TestCase(0)]
    [TestCase(-10)]
    public async Task AmountNotPositive_Returns400(decimal amount)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, world.UsdAccountId, amount));
        await ShouldRejectAsync(world, BuyBody(world.NzdAccountId, world.UsdAccountId, amount));
    }

    [Test]
    public async Task TooManyDecimalPlaces_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, world.UsdAccountId, 10.001m));
        await ShouldRejectAsync(world, BuyBody(world.NzdAccountId, world.UsdAccountId, 10.001m));
    }

    [Test]
    public async Task ComputedAmountOfZero_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);

        // 0.01 NZD × 0.5912 = 0.005912 → 0.01 USD; use a rate that rounds to 0.
        await SetRateAsync("NZD", "USD", 0.10000000m, new DateOnly(2026, 11, 10));
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 0.01m, expectedRate: 0.10000000m);

        await ShouldRejectAsync(world, body);
    }

    [Test]
    public async Task MissingRate_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var aud = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned AUD", "AUD");

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, aud, 10m));
    }

    [Test]
    public async Task InversePairOnly_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await InsertUsdAsync(world, 100m);
        var aud = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned AUD", "AUD");

        // Only AUD → USD is stored; USD → AUD is not derived from it (R9).
        await SetRateAsync("AUD", "USD", 0.65000000m, RateAsOf);

        await ShouldRejectAsync(world, SellBody(world.UsdAccountId, aud, 10m, expectedRate: 1.53846154m));
    }

    [Test]
    public async Task StaleRate_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body["bookedAt"] = "2026-11-14T09:00:00Z"; // 5 days after the 2026-11-09 rate

        await ShouldRejectAsync(world, body);
    }

    [Test]
    public async Task RateFourDaysOld_IsAccepted()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body["bookedAt"] = "2026-11-13T09:00:00Z";

        (await PostAsync(world.Legs.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task MissingExpectedRate_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body.Remove("expectedRate");

        await ShouldRejectAsync(world, body);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ClosedFromOrToAccount_Returns400(bool closeFrom)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        await LegAccountsFixture.CloseAccountDirectlyAsync(closeFrom ? world.NzdAccountId : world.UsdAccountId);

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, world.UsdAccountId, 10m));
    }

    [Test]
    public async Task DisabledCustomer_Returns400TargetUser()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        await LegAccountsFixture.DisableCustomerAsync(world.Legs.Ned.Id);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        var response = await PostAsync(
            world.Legs.TenantAdmin.AccessToken, SellBody(world.NzdAccountId, world.UsdAccountId, 10m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task InsufficientCash_Returns400AndWritesNothing()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);

        await ShouldRejectAsync(world, SellBody(world.NzdAccountId, world.UsdAccountId, 100.01m));
        (await CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(100m);
        (await CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.UsdAccountId)).ShouldBe(0m);
        (await CountFxDetailsAsync()).ShouldBe(0);
    }

    [TestCase("type", "FxConversion")]
    [TestCase("accountId", "8f0c9a52-1d0e-4bd1-9a7a-3b0f8b3b1b11")]
    [TestCase("currency", "NZD")]
    [TestCase("amount", "10")]
    [TestCase("rate", "0.5912")]
    public async Task ForbiddenBodyField_Returns400(string field, string value)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body[field] = value;

        await ShouldRejectAsync(world, body);
    }

    [TestCase("cash")]
    [TestCase("security")]
    [TestCase("legs")]
    public async Task LegsArrayInBody_Returns400(string field)
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body[field] = new[] { new { amount = 10m, currency = "NZD" } };

        await ShouldRejectAsync(world, body);
    }

    [Test]
    public async Task IdempotencyKey_IsRequiredReplaysAndRejectsADifferentBody()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);

        (await TransactionHttp.Send(HttpMethod.Post, FxPath, world.Legs.TenantAdmin.AccessToken, body, idempotencyKey: null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var key = Guid.NewGuid();
        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(world.Legs.TenantAdmin.AccessToken, body, key));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        var replay = await PostAsync(world.Legs.TenantAdmin.AccessToken, body, key);
        (await LegAccountsFixture.ReadIdAsync(replay)).ShouldBe(id);

        (await PostAsync(world.Legs.TenantAdmin.AccessToken, SellBody(world.NzdAccountId, world.UsdAccountId, 11m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(90m);
    }

    [Test]
    public async Task Replay_ReturnsTheOriginalIdAfterTheRateChanged()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var body = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        var key = Guid.NewGuid();
        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(world.Legs.TenantAdmin.AccessToken, body, key));

        await SetRateAsync("NZD", "USD", 0.60000000m, new DateOnly(2026, 11, 10));

        (await LegAccountsFixture.ReadIdAsync(await PostAsync(world.Legs.TenantAdmin.AccessToken, body, key)))
            .ShouldBe(id);
    }

    [Test]
    public async Task IdempotencyKey_UsedOnTheTransferRouteIsAConflict()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);
        var savings = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned savings");
        var key = Guid.NewGuid();
        (await InternalTransferTests.PostAsync(
                world.Legs.TenantAdmin.AccessToken,
                InternalTransferTests.Body(world.NzdAccountId, savings, 10m),
                key))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(world.Legs.TenantAdmin.AccessToken, SellBody(world.NzdAccountId, world.UsdAccountId, 10m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task TenantAdminOmitsTenantId_SystemAdminMustSendIt()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(100m);

        var withTenant = SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        withTenant["tenantId"] = world.Legs.Tenant.PublicId;
        (await PostAsync(world.Legs.TenantAdmin.AccessToken, withTenant))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var system = await AccountHttp.SignInSystemAdmin();
        (await PostAsync(system.AccessToken, SellBody(world.NzdAccountId, world.UsdAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(system.AccessToken, withTenant))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    internal static Dictionary<string, object?> SellBody(
        Guid fromAccountId,
        Guid toAccountId,
        decimal sellAmount,
        string? memo = null,
        decimal expectedRate = NzdUsd) =>
        new()
        {
            ["fromAccountId"] = fromAccountId,
            ["toAccountId"] = toAccountId,
            ["sellAmount"] = sellAmount,
            ["expectedRate"] = expectedRate,
            ["bookedAt"] = BookedAt,
            ["memo"] = memo,
            ["reference"] = null
        };

    internal static Dictionary<string, object?> BuyBody(
        Guid fromAccountId,
        Guid toAccountId,
        decimal buyAmount,
        decimal expectedRate = NzdUsd) =>
        new()
        {
            ["fromAccountId"] = fromAccountId,
            ["toAccountId"] = toAccountId,
            ["buyAmount"] = buyAmount,
            ["expectedRate"] = expectedRate,
            ["bookedAt"] = BookedAt,
            ["memo"] = null,
            ["reference"] = null
        };

    internal static Task<HttpResponseMessage> PostAsync(string accessToken, object body, Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, FxPath, accessToken, body, key ?? Guid.NewGuid());

    internal static Task<decimal> CashBalanceAsync(string accessToken, Guid accountId) =>
        InternalTransferTests.CashBalanceAsync(accessToken, accountId);

    internal static async Task SetRateAsync(string from, string to, decimal rate, DateOnly asOf)
    {
        await using var scope = FunctionalTestSetup.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await db.FxRates.SingleOrDefaultAsync(row =>
            row.BaseCurrency == from && row.QuoteCurrency == to && row.AsOfDate == asOf);
        if (existing is null)
        {
            db.FxRates.Add(new FxRate
            {
                BaseCurrency = from,
                QuoteCurrency = to,
                Rate = rate,
                AsOfDate = asOf,
                Source = "Frankfurter",
                FetchedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Rate = rate;
        }

        await db.SaveChangesAsync();
    }

    internal static async Task<(string Base, string Quote, decimal Rate, DateOnly AsOf, string Source)?> FxDetailAsync(
        Guid transactionPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transactionId = await db.Transactions
            .Where(row => row.PublicId == transactionPublicId)
            .Select(row => row.Id)
            .SingleAsync();
        var rows = await db.Database.SqlQuery<FxDetailRow>($"""
            SELECT [BaseCurrency], [QuoteCurrency], [Rate], [RateAsOfDate], [RateSource]
            FROM [TransactionFxDetails] WHERE [TransactionId] = {transactionId}
            """).ToListAsync();
        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows.Single();
        return (row.BaseCurrency.Trim(), row.QuoteCurrency.Trim(), row.Rate, row.RateAsOfDate, row.RateSource);
    }

    internal static async Task<int> CountFxDetailsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [TransactionFxDetails]").SingleAsync();
    }

    internal static async Task ShouldRejectAsync(FxWorld world, object body, string? accessToken = null)
    {
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);
        var detailsBefore = await CountFxDetailsAsync();

        (await PostAsync(accessToken ?? world.Legs.TenantAdmin.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await CountFxDetailsAsync()).ShouldBe(detailsBefore);
    }

    private static Task InsertUsdAsync(FxWorld world, decimal amount) =>
        LegAccountsFixture.InsertCashTransactionAsync(
            world.Legs.Tenant.Id, TransactionType.TransferIn, (world.UsdAccountId, amount));

    internal sealed record FxDetailRow(
        string BaseCurrency,
        string QuoteCurrency,
        decimal Rate,
        DateOnly RateAsOfDate,
        string RateSource);
}

/// <summary>
/// The leg-accounts world plus a USD Bank account for Ned and the NZD → USD rate 0.5912 as of 2026-11-09.
/// Ned's NZD account is <see cref="NzdAccountId"/>.
/// </summary>
internal sealed record FxWorld(LegAccountsWorld Legs, Guid UsdAccountId)
{
    public Guid NzdAccountId => Legs.NedAccountId;

    public static async Task<FxWorld> CreateAsync()
    {
        var legs = await LegAccountsFixture.CreateAsync();
        var usd = await InternalTransferTests.OpenAccountAsync(
            legs.TenantAdmin.AccessToken, legs.Ned.PublicId, "Ned USD", "USD");
        await FxConversionTests.SetRateAsync("NZD", "USD", FxConversionTests.NzdUsd, FxConversionTests.RateAsOf);
        return new FxWorld(legs, usd);
    }

    public Task FundNzdAsync(decimal amount) =>
        LegAccountsFixture.InsertCashTransactionAsync(Legs.Tenant.Id, TransactionType.TransferIn, (NzdAccountId, amount));
}
