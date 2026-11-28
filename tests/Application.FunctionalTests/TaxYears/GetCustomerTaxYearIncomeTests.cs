using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>
/// FIF income R1–R27 through <c>GET /users/customers/{customerId}/tax-years/{taxYear}</c>: <c>fifIncome</c> on the item
/// and on each tax holding.
/// </summary>
[Parallelizable]
public class GetCustomerTaxYearIncomeTests : TestBase
{
    private sealed record Example3World(string Token, Guid CustomerId, Guid Account, Guid Qqq, Guid Rrr, Guid? Sss);

    [TestCase(2025, 0.00, 0.00, 752.78)]
    [TestCase(2026, 13_108.48, 655.42, 1_740.87)]
    public async Task Example1_TheWorkedExampleOfTaxYearEvents(int taxYear, decimal openingValue, decimal fdrIncome, decimal cvAmount)
    {
        var world = await TaxYearWorld.CreateAsync();

        var year = await ReadAsync(world.Token, world.Customer.PublicId, taxYear);

        var income = FifIncomeOf(year);
        Fdr(income).ShouldBe((openingValue, fdrIncome, 0.00m, 0.00m, 0.00m, fdrIncome));
        Cv(income).ShouldBe((cvAmount, cvAmount));
        (income.GetProperty("lower").GetString(), income.GetProperty("complete").GetBoolean()).ShouldBe(("Fdr", true));
        var holding = HoldingIncome(year, "AAA");
        (holding.GetProperty("status").GetString(), holding.GetProperty("fdr").GetProperty("amount").GetDecimal()).ShouldBe(("Computed", fdrIncome));
        holding.GetProperty("cv").GetProperty("amount").GetDecimal().ShouldBe(cvAmount);
    }

    [Test]
    public async Task Example3_IsTheFieldsOfTheSpec()
    {
        var world = await Example3Async();

        var year = await ReadAsync(world.Token, world.CustomerId, 2026);

        var income = FifIncomeOf(year);
        income.EnumerateObject().Select(property => property.Name).ShouldBe(["rate", "fdr", "cv", "lower", "complete", "incomplete"]);
        income.GetProperty("rate").GetRawText().ShouldBe("0.05");
        income.GetProperty("fdr").EnumerateObject().Select(property => property.Name)
            .ShouldBe(["openingValue", "amount", "peakHoldingAmount", "quickSaleGainAmount", "quickSaleAdjustment", "income"]);
        Fdr(income).ShouldBe((20_000.00m, 1_000.00m, 700.00m, 360.00m, 360.00m, 1_360.00m));
        Cv(income).ShouldBe((-1_740.00m, 0.00m));
        income.GetProperty("cv").GetProperty("income").GetRawText().ShouldBe("0.00");
        (income.GetProperty("lower").GetString(), income.GetProperty("complete").GetBoolean()).ShouldBe(("Cv", true));
        income.GetProperty("incomplete").GetArrayLength().ShouldBe(0);

        var qqq = HoldingIncome(year, "QQQ");
        qqq.EnumerateObject().Select(property => property.Name).ShouldBe(["status", "reason", "fdr", "quickSale", "cv"]);
        (qqq.GetProperty("status").GetString(), qqq.GetProperty("reason").ValueKind).ShouldBe(("Computed", JsonValueKind.Null));
        (qqq.GetProperty("fdr").GetProperty("openingValue").GetDecimal(), qqq.GetProperty("fdr").GetProperty("amount").GetDecimal())
            .ShouldBe((20_000.00m, 1_000.00m));
        var sale = qqq.GetProperty("quickSale");
        sale.EnumerateObject().Select(property => property.Name).ShouldBe(
        [
            "openingQuantity", "peakQuantity", "closingQuantity", "peakHoldingDifferential", "purchasedQuantity", "purchaseCost", "averageCost",
            "peakHoldingAmount", "matchedQuantity", "proceeds", "dividends", "cost", "gainAmount", "matches", "sales", "dividendShares"
        ]);
        Decimals(sale, "openingQuantity", "peakQuantity", "closingQuantity", "peakHoldingDifferential", "purchasedQuantity", "purchaseCost",
                "averageCost", "peakHoldingAmount", "matchedQuantity", "proceeds", "dividends", "cost", "gainAmount")
            .ShouldBe([2_000m, 3_200m, 2_200m, 1_000m, 1_700m, 18_700.00m, 11m, 550.00m, 1_200m, 15_400.00m, 260.00m, 13_200.00m, 2_460.00m]);
        sale.GetProperty("matches").EnumerateArray()
            .Select(match => (match.GetProperty("purchaseSequence").GetInt32(), match.GetProperty("saleSequence").GetInt32(),
                match.GetProperty("quantity").GetDecimal()))
            .ShouldBe([(3, 5, 400m), (1, 5, 600m), (1, 7, 200m)]);
        sale.GetProperty("sales").EnumerateArray()
            .Select(s => (s.GetProperty("sequence").GetInt32(), s.GetProperty("quantity").GetDecimal(), s.GetProperty("matchedQuantity").GetDecimal(),
                s.GetProperty("amount").GetDecimal(), s.GetProperty("proceeds").GetDecimal()))
            .ShouldBe([(5, 1_000m, 1_000m, 13_000.00m, 13_000.00m), (7, 500m, 200m, 6_000.00m, 2_400.00m)]);
        sale.GetProperty("dividendShares").EnumerateArray()
            .Select(d => (d.GetProperty("sequence").GetInt32(), d.GetProperty("amount").GetDecimal(), d.GetProperty("heldQuantity").GetDecimal(),
                d.GetProperty("matchedQuantity").GetDecimal(), d.GetProperty("share").GetDecimal()))
            .ShouldBe([(2, 700.00m, 2_800m, 800m, 200.00m), (6, 660.00m, 2_200m, 200m, 60.00m)]);
        Decimals(qqq.GetProperty("cv"), "openingValue", "closingValue", "sales", "dividends", "purchases", "amount")
            .ShouldBe([20_000.00m, 23_100.00m, 19_000.00m, 1_360.00m, 18_700.00m, 4_760.00m]);

        var rrr = HoldingIncome(year, "RRR");
        var rrrSale = rrr.GetProperty("quickSale");
        Decimals(rrrSale, "peakHoldingDifferential", "averageCost", "peakHoldingAmount", "matchedQuantity", "proceeds", "cost", "gainAmount")
            .ShouldBe([100m, 30m, 150.00m, 100m, 900.00m, 3_000.00m, -2_100.00m]);
        rrrSale.GetProperty("dividendShares").GetArrayLength().ShouldBe(0);
        rrr.GetProperty("cv").GetProperty("amount").GetDecimal().ShouldBe(-6_500.00m);
    }

    [Test]
    public async Task AnAttributingNoFdrHolding_BesideThem_IsListedAndNotComputed_AndTheTotalsStay()
    {
        var world = await Example3Async(withSss: true);

        var year = await ReadAsync(world.Token, world.CustomerId, 2026);

        var income = FifIncomeOf(year);
        Fdr(income).ShouldBe((20_000.00m, 1_000.00m, 700.00m, 360.00m, 360.00m, 1_360.00m));
        Cv(income).ShouldBe((-1_740.00m, 0.00m));
        (income.GetProperty("lower").GetString(), income.GetProperty("complete").GetBoolean()).ShouldBe(("Cv", false));
        Incomplete(income).ShouldBe([("FdrNotAllowed", world.Sss!.Value)]);
        NotComputed(HoldingOf(year, "SSS")).ShouldBe(("NotComputed", "FdrNotAllowed"));
        // The tax year and the holding stay complete.
        (year.GetProperty("complete").GetBoolean(), HoldingOf(year, "SSS").GetProperty("complete").GetBoolean()).ShouldBe((true, true));
    }

    [Test]
    public async Task AHoldingWithNoClosingPrice_IsHoldingIncomplete_AndNoTotalIsGiven()
    {
        var world = await Example3Async(rrrClosingPrice: false);

        var year = await ReadAsync(world.Token, world.CustomerId, 2026);

        var income = FifIncomeOf(year);
        foreach (var field in new[] { "fdr", "cv", "lower" })
        {
            income.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }

        (income.GetProperty("rate").GetDecimal(), income.GetProperty("complete").GetBoolean()).ShouldBe((0.05m, false));
        Incomplete(income).ShouldBe([("HoldingIncomplete", world.Rrr)]);
        NotComputed(HoldingOf(year, "RRR")).ShouldBe(("NotComputed", "HoldingIncomplete"));
        HoldingIncome(year, "QQQ").GetProperty("fdr").GetProperty("amount").GetDecimal().ShouldBe(1_000.00m);
    }

    [Test]
    public async Task APurchaseReversedLater_IsInNoQuickSale()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var ttt = await CustodialHttp.CreateInstrumentAsync(token, "TTT", "NZD", price: null);
        await ClassifyAsync(token, ttt, "US", 2026, "Attributing");
        await PostAsync(token, Cash(account, "TransferIn", 10_000.00m, "2025-04-01T00:00:00Z"));
        await PostAsync(token, Trade(account, "Buy", ttt, 100m, 10.00m, "2025-05-01T01:00:00Z"));
        var mistaken = await PostAsync(token, Trade(account, "Buy", ttt, 100m, 20.00m, "2025-05-02T01:00:00Z"));
        await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(token, mistaken));
        await PostAsync(token, Trade(account, "Sell", ttt, 100m, 12.00m, "2025-06-01T01:00:00Z"));

        var year = await ReadAsync(token, customer.PublicId, 2026);

        var sale = HoldingIncome(year, "TTT").GetProperty("quickSale");
        Decimals(sale, "purchasedQuantity", "purchaseCost", "averageCost", "matchedQuantity", "gainAmount").ShouldBe([100m, 1_000.00m, 10m, 100m, 200.00m]);
        sale.GetProperty("matches").EnumerateArray()
            .Select(match => (match.GetProperty("purchaseSequence").GetInt32(), match.GetProperty("saleSequence").GetInt32()))
            .ShouldBe([(1, 2)]);
    }

    [Test]
    public async Task AnEntryBackdatedOnTheExternalAccountIntoAnEarlierTaxYear_ChangesTheFdrAmountOfTheNextRead()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var uuu = await CustodialHttp.CreateInstrumentAsync(token, "UUU", "NZD", price: null);
        await ClassifyAsync(token, uuu, "US", 2025, "Attributing");
        await PostAsync(token, Cash(account, "TransferIn", 10_000.00m, "2024-05-01T00:00:00Z"));
        await PostAsync(token, Trade(account, "Buy", uuu, 100m, 15.00m, "2024-06-03T01:00:00Z"));
        await AddPriceAsync(uuu, "NZD", new DateOnly(2025, 3, 31), 20.00m);
        await AddPriceAsync(uuu, "NZD", new DateOnly(2026, 3, 31), 22.00m);
        var before = FifIncomeOf(await ReadAsync(token, customer.PublicId, 2026));

        await PostAsync(token, Trade(account, "Buy", uuu, 100m, 16.00m, "2024-07-01T01:00:00Z"));
        var after = FifIncomeOf(await ReadAsync(token, customer.PublicId, 2026));

        (Fdr(before).OpeningValue, Fdr(before).Amount).ShouldBe((2_000.00m, 100.00m));
        (Fdr(after).OpeningValue, Fdr(after).Amount).ShouldBe((4_000.00m, 200.00m));
    }

    /// <summary>
    /// Example 3 of fif-income §1.1 on one NZD External account: <c>QQQ</c> as example 2, held since tax year 2025 and
    /// split 2 for 1, and <c>RRR</c> bought and partly sold at a loss; with <paramref name="withSss"/>, also
    /// <c>SSS</c>, <c>AttributingNoFdr</c>. Every Instrument is quoted in NZD, so every figure is the one booked.
    /// </summary>
    private static async Task<Example3World> Example3Async(bool rrrClosingPrice = true, bool withSss = false)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var b = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var qqq = await CustodialHttp.CreateInstrumentAsync(token, "QQQ", "NZD", price: null);
        var rrr = await CustodialHttp.CreateInstrumentAsync(token, "RRR", "NZD", price: null);
        await ClassifyAsync(token, qqq, "US", 2025, "Attributing");
        await ClassifyAsync(token, rrr, "US", 2025, "Attributing");
        await PostAsync(token, Cash(b, "TransferIn", 100_000.00m, "2024-05-01T00:00:00Z"));
        await PostAsync(token, Trade(b, "Buy", qqq, 1_000m, 15.00m, "2024-06-03T01:00:00Z"));

        await PostAsync(token, Trade(b, "Buy", qqq, 400m, 21.975m, "2025-04-10T01:00:00Z", fee: 10.00m));
        await DividendAsync(token, b, qqq, 1_400m, 700.00m, 105.00m, "2025-05-20T01:00:00Z");
        await PostAsync(token, Trade(b, "Buy", qqq, 200m, 24.95m, "2025-06-15T01:00:00Z", fee: 10.00m));
        await PostAsync(token, Split(b, qqq, 3_200m, "2025-07-01T00:00:00Z"));
        await PostAsync(token, Trade(b, "Sell", qqq, 1_000m, 13.015m, "2025-08-10T01:00:00Z", fee: 15.00m));
        await DividendAsync(token, b, qqq, 2_200m, 660.00m, 99.00m, "2025-09-05T01:00:00Z");
        await PostAsync(token, Trade(b, "Sell", qqq, 500m, 12.02m, "2025-11-03T01:00:00Z", fee: 10.00m));
        await PostAsync(token, Trade(b, "Buy", qqq, 500m, 9.78m, "2026-02-12T01:00:00Z", fee: 10.00m));
        await AddPriceAsync(qqq, "NZD", new DateOnly(2025, 3, 31), 20.00m);
        await AddPriceAsync(qqq, "NZD", new DateOnly(2026, 3, 31), 10.50m);

        await PostAsync(token, Trade(b, "Buy", rrr, 300m, 30.00m, "2025-05-01T01:00:00Z"));
        await PostAsync(token, Trade(b, "Sell", rrr, 100m, 9.00m, "2025-12-01T01:00:00Z"));
        if (rrrClosingPrice)
        {
            await AddPriceAsync(rrr, "NZD", new DateOnly(2026, 3, 31), 8.00m);
        }

        Guid? sss = null;
        if (withSss)
        {
            sss = await CustodialHttp.CreateInstrumentAsync(token, "SSS", "NZD", price: null);
            await ClassifyAsync(token, sss.Value, "US", 2025, "AttributingNoFdr");
            await PostAsync(token, Trade(b, "Buy", sss.Value, 100m, 50.00m, "2024-06-04T01:00:00Z"));
            await AddPriceAsync(sss.Value, "NZD", new DateOnly(2025, 3, 31), 50.00m);
            await AddPriceAsync(sss.Value, "NZD", new DateOnly(2026, 3, 31), 40.00m);
        }

        return new Example3World(token, customer.PublicId, b, qqq, rrr, sss);
    }

    /// <summary>A dividend paid into an External account, with 15 % foreign tax withheld (external-income R11).</summary>
    private static async Task DividendAsync(
        string token, Guid accountId, Guid instrumentId, decimal shares, decimal gross, decimal foreignTax, string bookedAt)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["instrumentId"] = instrumentId,
            ["quantity"] = shares,
            ["grossAmount"] = gross,
            ["taxSource"] = "Foreign",
            ["bookedAt"] = bookedAt,
            ["taxes"] = ExternalDividendTests.Taxes(("ForeignWithholding", 15m, foreignTax))
        };
        await CustodialHttp.ReadIdAsync(await ExternalDividendTests.PostAsync(token, body));
    }

    private static JsonElement FifIncomeOf(JsonElement year)
    {
        year.TryGetProperty("fifIncome", out var income).ShouldBeTrue("fifIncome");
        income.ValueKind.ShouldBe(JsonValueKind.Object);
        return income;
    }

    private static JsonElement HoldingIncome(JsonElement year, string symbol)
    {
        var income = HoldingOf(year, symbol).GetProperty("fifIncome");
        income.ValueKind.ShouldBe(JsonValueKind.Object, symbol);
        return income;
    }

    private static (decimal OpeningValue, decimal Amount, decimal PeakHoldingAmount, decimal QuickSaleGainAmount, decimal QuickSaleAdjustment,
        decimal Income) Fdr(JsonElement income)
    {
        var fdr = income.GetProperty("fdr");
        fdr.ValueKind.ShouldBe(JsonValueKind.Object);
        return (fdr.GetProperty("openingValue").GetDecimal(), fdr.GetProperty("amount").GetDecimal(), fdr.GetProperty("peakHoldingAmount").GetDecimal(),
            fdr.GetProperty("quickSaleGainAmount").GetDecimal(), fdr.GetProperty("quickSaleAdjustment").GetDecimal(), fdr.GetProperty("income").GetDecimal());
    }

    private static (decimal Amount, decimal Income) Cv(JsonElement income)
    {
        var cv = income.GetProperty("cv");
        cv.ValueKind.ShouldBe(JsonValueKind.Object);
        return (cv.GetProperty("amount").GetDecimal(), cv.GetProperty("income").GetDecimal());
    }

    private static List<(string?, Guid)> Incomplete(JsonElement income) =>
        income.GetProperty("incomplete").EnumerateArray()
            .Select(item => (item.GetProperty("reason").GetString(), item.GetProperty("instrumentId").GetGuid()))
            .ToList();

    /// <summary>The status and the reason of a tax holding's income, after checking that it has no figure.</summary>
    private static (string?, string?) NotComputed(JsonElement holding)
    {
        holding.TryGetProperty("fifIncome", out var income).ShouldBeTrue("fifIncome");
        income.ValueKind.ShouldBe(JsonValueKind.Object);
        foreach (var field in new[] { "fdr", "quickSale", "cv" })
        {
            income.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }

        return (income.GetProperty("status").GetString(), income.GetProperty("reason").GetString());
    }

    private static List<decimal> Decimals(JsonElement element, params string[] names) =>
        names.Select(name => element.GetProperty(name).GetDecimal()).ToList();
}
