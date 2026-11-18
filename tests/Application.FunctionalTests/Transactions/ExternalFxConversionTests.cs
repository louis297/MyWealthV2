using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>
/// External caller numbers R15–R20: a conversion between External Accounts takes both amounts, derives the rate and
/// reads no stored rate; the quote refuses External Accounts. Custodial conversions are unchanged.
/// </summary>
[Parallelizable]
public class ExternalFxConversionTests : TestBase
{
    private const string QuotePath = "/transactions/fx/quote";
    private static readonly DateOnly BookedDate = new(2026, 11, 10);

    [Test]
    public async Task BothAmounts_BookTheLegsAsEnteredAndStoreTheDerivedRateWithoutAStoredRate()
    {
        var world = await WorldAsync(2000m);

        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(world,
            FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 845.74m, 500.00m, memo: "Bank conversion")));

        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NzdAccountId, -845.74m),
            (world.UsdAccountId, 500.00m)
        ]);
        (await FxConversionTests.FxDetailAsync(id)).ShouldBe(("NZD", "USD", 0.59119824m, BookedDate, "Caller"));
        using var item = await CustodialHttp.GetTransactionAsync(world.Legs.TenantAdmin.AccessToken, id);
        item.RootElement.GetProperty("type").GetString().ShouldBe("FxConversion");
        item.RootElement.GetProperty("memo").GetString().ShouldBe("Bank conversion");
        var fx = item.RootElement.GetProperty("fx");
        fx.GetProperty("baseCurrency").GetString().ShouldBe("NZD");
        fx.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        fx.GetProperty("rate").GetDecimal().ShouldBe(0.59119824m);
        fx.GetProperty("rateAsOfDate").GetString().ShouldBe("2026-11-10");
    }

    [Test]
    public async Task ZeroDecimalCurrency_DerivesTheRateToEightDecimals()
    {
        var world = await WorldAsync();
        var jpy = await world.OpenAccountAsync(world.Legs.Ned.PublicId, "Ned JPY", "JPY");
        await LegAccountsFixture.InsertCashTransactionAsync(world.Legs.Tenant.Id, TransactionType.TransferIn, (jpy, 200000m));

        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(world,
            FxConversionTests.ExternalBody(jpy, world.UsdAccountId, 100000m, 612.40m)));

        (await FxConversionTests.FxDetailAsync(id))!.Value.Rate.ShouldBe(0.00612400m);
        await ShouldRejectAsync(world, FxConversionTests.ExternalBody(jpy, world.UsdAccountId, 100000.5m, 612.40m), "SellAmount");
    }

    [Test]
    public async Task OneAmount_Returns400OnTheMissingField()
    {
        var world = await WorldAsync();
        var sellOnly = FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 10m, 5m);
        sellOnly.Remove("buyAmount");
        var buyOnly = FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 10m, 5m);
        buyOnly.Remove("sellAmount");

        (await ShouldRejectAsync(world, sellOnly, "BuyAmount"))
            .ShouldBe(["An External conversion needs both sellAmount and buyAmount."]);
        (await ShouldRejectAsync(world, buyOnly, "SellAmount"))
            .ShouldBe(["An External conversion needs both sellAmount and buyAmount."]);
    }

    [Test]
    public async Task ExpectedRate_Returns400()
    {
        var world = await WorldAsync();
        var body = FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 10m, 5.91m);
        body["expectedRate"] = 0.591m;

        (await ShouldRejectAsync(world, body, "ExpectedRate"))
            .ShouldBe(["expectedRate cannot be set on an External conversion. The rate comes from the two amounts."]);
    }

    [Test]
    public async Task TooManyDecimalPlacesOnEitherAmount_Returns400()
    {
        var world = await WorldAsync();

        await ShouldRejectAsync(world, FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 10.001m, 5m), "SellAmount");
        await ShouldRejectAsync(world, FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 10m, 5.001m), "BuyAmount");
    }

    [Test]
    public async Task RateThatRoundsToZeroOrDoesNotFit_Returns400OnBuyAmount()
    {
        var world = await WorldAsync(10_000_000_000m);

        await ShouldRejectAsync(world, FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 9_000_000_000m, 0.01m), "BuyAmount");
        await ShouldRejectAsync(world, FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 0.01m, 1_000_000_000m), "BuyAmount");
    }

    [Test]
    public async Task InsufficientCash_Returns400AndWritesNothing()
    {
        var world = await WorldAsync(100m);

        await ShouldRejectAsync(world, FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 100.01m, 59.12m), string.Empty);
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(100m);
    }

    [Test]
    public async Task Reverse_RestoresTheEnteredAmountsAndCopiesTheFxDetail()
    {
        var world = await WorldAsync(1000m);
        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(world,
            FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 845.74m, 500.00m)));

        var reversalId = await LegAccountsFixture.ReadIdAsync(
            await LegAccountsFixture.ReverseAsync(world.Legs.TenantAdmin.AccessToken, id));

        (await LegAccountsFixture.CashLegsAsync(reversalId)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.UsdAccountId, -500.00m),
            (world.NzdAccountId, 845.74m)
        ]);
        (await FxConversionTests.FxDetailAsync(reversalId)).ShouldBe(("NZD", "USD", 0.59119824m, BookedDate, "Caller"));
        (await FxConversionTests.CashBalanceAsync(world.Legs.TenantAdmin.AccessToken, world.NzdAccountId)).ShouldBe(1000m);
    }

    [Test]
    public async Task IdempotencyKey_ReplaysTheSameBodyAndRejectsAnotherAmount()
    {
        var world = await WorldAsync(1000m);
        var key = Guid.NewGuid();
        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(world,
            FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 100m, 59.12m), key));

        (await LegAccountsFixture.ReadIdAsync(await PostAsync(world,
            FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 100m, 59.12m), key))).ShouldBe(id);
        (await PostAsync(world, FxConversionTests.ExternalBody(world.NzdAccountId, world.UsdAccountId, 100m, 59.13m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Quote_BetweenTwoExternalAccounts_Returns400OnFromAccountId()
    {
        var world = await WorldAsync();

        var errors = await CustodialHttp.ErrorsAsync(await QuoteAsync(world.Legs.TenantAdmin.AccessToken, new Dictionary<string, object?>
        {
            ["fromAccountId"] = world.NzdAccountId,
            ["toAccountId"] = world.UsdAccountId,
            ["sellAmount"] = 10m,
            ["bookedAt"] = FxConversionTests.BookedAt
        }));

        errors.Count.ShouldBe(1);
        errors["FromAccountId"].ShouldBe(["A quote applies to Custodial accounts only. On an External account enter both amounts."]);
    }

    [Test]
    public async Task Quote_BetweenExternalAndCustodial_StillReturns400OnToAccountId()
    {
        var world = await WorldAsync();
        var custodialUsd = await CustodialHttp.OpenCustodialAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned managed USD", "USD");

        var errors = await CustodialHttp.ErrorsAsync(await QuoteAsync(world.Legs.TenantAdmin.AccessToken, new Dictionary<string, object?>
        {
            ["fromAccountId"] = world.NzdAccountId,
            ["toAccountId"] = custodialUsd,
            ["sellAmount"] = 10m
        }));

        errors.Count.ShouldBe(1);
        errors.ShouldContainKey("ToAccountId");
    }

    [Test]
    public async Task CustodialConversion_StillNeedsExactlyOneAmountAndAnExpectedRate()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(1000m);
        var both = FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        both["buyAmount"] = 5.91m;
        var noExpectedRate = FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        noExpectedRate.Remove("expectedRate");

        (await ShouldRejectAsync(world, both, "SellAmount")).ShouldBe(["Send exactly one of sellAmount and buyAmount."]);
        (await ShouldRejectAsync(world, noExpectedRate, "ExpectedRate")).ShouldBe(["Expected rate is required. Get a quote first."]);
        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, new Dictionary<string, object?>
        {
            ["fromAccountId"] = world.NzdAccountId,
            ["toAccountId"] = world.UsdAccountId,
            ["sellAmount"] = 10m
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AnotherTenantsExternalAccount_Returns404Not400(bool otherTenantIsFrom)
    {
        var world = await WorldAsync(100m);
        var (_, _, southCustomer, south) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var southAccount = await AccountHttp.OpenAccountAsync(
            south.AccessToken, southCustomer.PublicId, "South USD", "Bank", otherTenantIsFrom ? "NZD" : "USD");
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);
        var body = otherTenantIsFrom
            ? FxConversionTests.ExternalBody(southAccount, world.UsdAccountId, 10m, 5.91m)
            : FxConversionTests.ExternalBody(world.NzdAccountId, southAccount, 10m, 5.91m);

        (await PostAsync(world, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // One amount on another tenant's Account is still 404, not the 400 for the missing amount.
        body.Remove("buyAmount");
        (await PostAsync(world, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await FxConversionTests.CountFxDetailsAsync()).ShouldBe(0);
    }

    /// <summary>The External FX world with no stored rate: a conversion between External Accounts reads none.</summary>
    private static async Task<FxWorld> WorldAsync(decimal nzdCash = 100m)
    {
        var world = await FxWorld.CreateExternalAsync();
        await FxConversionTests.DeleteRatesAsync();
        await world.FundNzdAsync(nzdCash);
        return world;
    }

    private static Task<HttpResponseMessage> PostAsync(FxWorld world, object body, Guid? key = null) =>
        FxConversionTests.PostAsync(world.Legs.TenantAdmin.AccessToken, body, key);

    private static Task<HttpResponseMessage> QuoteAsync(string accessToken, object body) =>
        TransactionHttp.Send(HttpMethod.Post, QuotePath, accessToken, body, idempotencyKey: null);

    /// <summary>Posts the body, expects a 400 with <paramref name="field"/> and no write; returns that field's messages.</summary>
    private static async Task<string[]> ShouldRejectAsync(FxWorld world, Dictionary<string, object?> body, string field)
    {
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);
        var details = await FxConversionTests.CountFxDetailsAsync();
        var key = Guid.NewGuid();

        var errors = await CustodialHttp.ErrorsAsync(await PostAsync(world, body, key));

        errors.ShouldContainKey(field);
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await FxConversionTests.CountFxDetailsAsync()).ShouldBe(details);
        (await CustodialHttp.IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
        return errors[field];
    }
}
