using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Valuations;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>
/// Tax-cost-threshold R1–R26 through <c>GET /users/customers/{customerId}/tax-years/{taxYear}</c>: the tax cost of each
/// tax holding and event, and the threshold test of the year.
/// </summary>
[Parallelizable]
public class GetCustomerTaxYearThresholdTests : TestBase
{
    [Test]
    public async Task Example3_OnTwoAccounts_IsTheFieldsOfTheSpec()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var b1 = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Managed", "Brokerage", "USD");
        var b2 = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var bbb = await CustodialHttp.CreateInstrumentAsync(token, "BBB", "USD", price: null);
        var ddd = await CustodialHttp.CreateInstrumentAsync(token, "DDD", "USD", price: null);
        var eee = await CustodialHttp.CreateInstrumentAsync(token, "EEE", "AUD", price: null);
        await ClassifyAsync(token, bbb, "US", 2026, "Attributing");
        await ClassifyAsync(token, ddd, "US", 2026, "Attributing");
        await ClassifyAsync(token, eee, "AU", 2026, "ExemptAsxListed");

        await PostAsync(token, Cash(b1, "TransferIn", 40_000.00m, "2025-04-01T00:00:00Z"));
        await PostAsync(token, Cash(b2, "TransferIn", 40_000.00m, "2025-04-01T00:00:00Z"));
        var u1 = await PostAsync(token, Trade(b2, "Buy", eee, 1_000m, 25.00m, "2025-04-02T01:00:00Z", fxRate: 1.2000m));
        var u2 = await PostAsync(token, Trade(b1, "Buy", bbb, 500m, 50.00m, "2025-05-12T20:00:00Z", fee: 10.00m));
        var u4 = await PostAsync(token, Trade(b1, "Buy", bbb, 40m, 55.00m, "2025-10-09T19:30:00Z", fee: 10.00m));
        var u5 = await PostAsync(token, Trade(b1, "Sell", bbb, 270m, 55.00m, "2025-11-20T20:10:00Z", fee: 10.00m));
        await ValuationRunDb.MakeCustodialAsync(b1, new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero));
        var u3 = await PostTransferInAsync(b1, ddd, 60m, 2_400.00m, new DateOnly(2024, 6, 14), new DateTimeOffset(2025, 8, 4, 3, 0, 0, TimeSpan.Zero));
        await AddRateAsync("USD", "NZD", new DateOnly(2024, 6, 14), 1.6300m);
        await AddRateAsync("USD", "NZD", new DateOnly(2025, 5, 12), 1.6900m);
        await AddRateAsync("USD", "NZD", new DateOnly(2025, 10, 9), 1.7500m);

        var year = await ReadAsync(token, customer.PublicId, 2026);

        var threshold = year.GetProperty("threshold");
        threshold.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["amount", "opening", "peak", "closing", "outcome", "complete", "incomplete", "steps"]);
        threshold.GetProperty("amount").GetDecimal().ShouldBe(50_000.00m);
        threshold.GetProperty("opening").GetRawText().ShouldBe("0.00");
        var peak = threshold.GetProperty("peak");
        (peak.GetProperty("total").GetDecimal(), peak.GetProperty("at").GetString(), peak.GetProperty("instrumentId").GetGuid(),
                peak.GetProperty("transactionId").GetGuid())
            .ShouldBe((50_046.40m, "2025-10-09T19:30:00Z", bbb, u4));
        threshold.GetProperty("closing").GetDecimal().ShouldBe(26_979.20m);
        (threshold.GetProperty("outcome").GetString(), threshold.GetProperty("complete").GetBoolean()).ShouldBe(("Above", true));
        threshold.GetProperty("incomplete").GetArrayLength().ShouldBe(0);
        threshold.GetProperty("steps").EnumerateArray()
            .Select(step => (step.GetProperty("at").GetString(), step.GetProperty("instrumentId").GetGuid(), step.GetProperty("eventSequence").GetInt32(),
                step.GetProperty("transactionId").GetGuid(), step.GetProperty("kind").GetString(), step.GetProperty("change").GetDecimal(),
                step.GetProperty("total").GetDecimal()))
            .ShouldBe([
                ("2025-05-12T20:00:00Z", bbb, 1, u2, "Acquisition", 42_266.90m, 42_266.90m),
                ("2025-08-04T03:00:00Z", ddd, 1, u3, "Arrival", 3_912.00m, 46_178.90m),
                ("2025-10-09T19:30:00Z", bbb, 2, u4, "Acquisition", 3_867.50m, 50_046.40m),
                ("2025-11-20T20:10:00Z", bbb, 3, u5, "Disposal", -23_067.20m, 26_979.20m)
            ]);

        var beta = HoldingOf(year, "BBB");
        Cost(beta).ShouldBe(("Known", 0.00m, 23_067.20m));
        beta.GetProperty("cost").GetProperty("unknown").ValueKind.ShouldBe(JsonValueKind.Null);
        beta.GetProperty("events").EnumerateArray().Select(EventCost)
            .ShouldBe([(42_266.90m, 42_266.90m, false), (3_867.50m, 46_134.40m, false), (-23_067.20m, 23_067.20m, false)]);

        var delta = HoldingOf(year, "DDD");
        Cost(delta).ShouldBe(("Known", 0.00m, 3_912.00m));
        var arrival = delta.GetProperty("events").EnumerateArray().ShouldHaveSingleItem();
        arrival.GetProperty("transactionId").GetGuid().ShouldBe(u3);
        arrival.GetProperty("carriedCost").GetProperty("acquiredOn").GetString().ShouldBe("2024-06-14");
        var fx = arrival.GetProperty("cost").GetProperty("fx");
        (fx.GetProperty("rate").GetDecimal(), fx.GetProperty("date").GetString()).ShouldBe((1.63m, "2024-06-14"));
        Strings(delta.GetProperty("notices")).ShouldContain("CarriedCost");

        var echo = HoldingOf(year, "EEE");
        var notRequired = echo.GetProperty("cost");
        notRequired.GetProperty("status").GetString().ShouldBe("NotRequired");
        foreach (var field in new[] { "opening", "closing", "unknown" })
        {
            notRequired.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }

        var purchase = echo.GetProperty("events").EnumerateArray().ShouldHaveSingleItem();
        purchase.GetProperty("transactionId").GetGuid().ShouldBe(u1);
        purchase.GetProperty("cost").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [TestCase(2022, 0.00, 55_000.00, 55_000.00, "Above")]
    [TestCase(2023, 55_000.00, 55_000.00, 55_000.00, "Above")]
    [TestCase(2024, 55_000.00, 55_000.00, 27_500.00, "Above")]
    [TestCase(2025, 27_500.00, 27_500.00, 27_500.00, "NotAbove")]
    public async Task Example2_Jax_OverFourTaxYears(int taxYear, decimal opening, decimal peak, decimal closing, string outcome)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Jax's broker", "Brokerage", "NZD");
        var aaa = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "NZD", price: null);
        var bbb = await CustodialHttp.CreateInstrumentAsync(token, "BBB", "NZD", price: null);
        var ccc = await CustodialHttp.CreateInstrumentAsync(token, "CCC", "NZD", price: null);
        await ClassifyAsync(token, aaa, "US", 2022, "Attributing");
        await ClassifyAsync(token, bbb, "US", 2022, "Attributing");
        await ClassifyAsync(token, ccc, "AU", (2024, "ExemptAsxListed"), (2025, "ExemptAsxListed"));

        await PostAsync(token, Cash(account, "TransferIn", 60_000.00m, "2022-03-01T00:00:00Z"));
        await PostAsync(token, Trade(account, "Buy", aaa, 200m, 149.925m, "2022-03-30T22:00:00Z", fee: 15.00m));
        var second = await PostAsync(token, Trade(account, "Buy", bbb, 500m, 49.98m, "2022-03-30T22:00:00Z", fee: 10.00m));
        await PostAsync(token, Trade(account, "Sell", aaa, 100m, 160.00m, "2024-01-15T21:00:00Z"));
        await PostAsync(token, Trade(account, "Sell", bbb, 250m, 64.00m, "2024-01-15T21:00:00Z"));
        await PostAsync(token, Trade(account, "Buy", ccc, 4_000m, 8.0375m, "2024-01-16T21:00:00Z"));

        var threshold = (await ReadAsync(token, customer.PublicId, taxYear)).GetProperty("threshold");

        (threshold.GetProperty("opening").GetDecimal(), threshold.GetProperty("peak").GetProperty("total").GetDecimal(),
                threshold.GetProperty("closing").GetDecimal(), threshold.GetProperty("outcome").GetString(), threshold.GetProperty("complete").GetBoolean())
            .ShouldBe((opening, peak, closing, outcome, true));
        if (taxYear == 2022)
        {
            threshold.GetProperty("peak").GetProperty("transactionId").GetGuid().ShouldBe(second);
        }
    }

    [Test]
    public async Task APurchaseInAnEarlierTaxYear_FromAUsdAccountWithNoStoredRate_IsCostUnknown_AndTheOutcomeUnknown()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "US broker", "Brokerage", "USD");
        var aaa = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "USD", price: null);
        await ClassifyAsync(token, aaa, "US", 2023, "Attributing");
        await PostAsync(token, Cash(account, "TransferIn", 10_000.00m, "2023-05-01T00:00:00Z"));
        var buy = await PostAsync(token, Trade(account, "Buy", aaa, 10m, 100.00m, "2023-05-20T02:00:00Z"));

        var year = await ReadAsync(token, customer.PublicId, 2026);

        var cost = HoldingOf(year, "AAA").GetProperty("cost");
        cost.GetProperty("status").GetString().ShouldBe("Unknown");
        cost.GetProperty("opening").ValueKind.ShouldBe(JsonValueKind.Null);
        var unknown = cost.GetProperty("unknown");
        (unknown.GetProperty("cause").GetString(), unknown.GetProperty("at").GetString(), unknown.GetProperty("transactionId").GetGuid())
            .ShouldBe(("NoRate", "2023-05-20T02:00:00Z", buy));
        var threshold = year.GetProperty("threshold");
        (threshold.GetProperty("outcome").GetString(), threshold.GetProperty("complete").GetBoolean()).ShouldBe(("Unknown", false));
        var reason = threshold.GetProperty("incomplete").EnumerateArray().ShouldHaveSingleItem();
        (reason.GetProperty("reason").GetString(), reason.GetProperty("instrumentId").GetGuid()).ShouldBe(("CostUnknown", aaa));
        (year.GetProperty("complete").GetBoolean(), HoldingOf(year, "AAA").GetProperty("complete").GetBoolean()).ShouldBe((false, false));
    }

    [Test]
    public async Task AnEntryBackdatedOnTheExternalAccountIntoAnEarlierTaxYear_ChangesTheTestOfTheNextRead()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var aaa = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "NZD", price: null);
        await ClassifyAsync(token, aaa, "US", 2025, "Attributing");
        await PostAsync(token, Cash(account, "TransferIn", 60_000.00m, "2024-05-01T00:00:00Z"));
        await PostAsync(token, Trade(account, "Buy", aaa, 300m, 100.00m, "2025-06-01T00:00:00Z"));

        var before = (await ReadAsync(token, customer.PublicId, 2026)).GetProperty("threshold");
        (before.GetProperty("opening").GetDecimal(), before.GetProperty("peak").GetProperty("total").GetDecimal(), before.GetProperty("outcome").GetString())
            .ShouldBe((0.00m, 30_000.00m, "NotAbove"));

        await PostAsync(token, Trade(account, "Buy", aaa, 250m, 100.00m, "2024-06-01T00:00:00Z"));

        var after = (await ReadAsync(token, customer.PublicId, 2026)).GetProperty("threshold");
        (after.GetProperty("opening").GetDecimal(), after.GetProperty("peak").GetProperty("total").GetDecimal(), after.GetProperty("outcome").GetString())
            .ShouldBe((25_000.00m, 55_000.00m, "Above"));
    }

    [Test]
    public async Task AReversedPurchase_ThatWouldHaveCrossedTheThreshold_LeavesNotAbove()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var aaa = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "NZD", price: null);
        await ClassifyAsync(token, aaa, "US", 2025, "Attributing");
        await PostAsync(token, Cash(account, "TransferIn", 60_000.00m, "2025-05-01T00:00:00Z"));
        await PostAsync(token, Trade(account, "Buy", aaa, 100m, 100.00m, "2025-05-02T00:00:00Z"));
        var mistake = await PostAsync(token, Trade(account, "Buy", aaa, 481.25m, 100.00m, "2025-06-03T04:00:00Z"));
        await CustodialHttp.ReverseAsync(token, mistake);

        var threshold = (await ReadAsync(token, customer.PublicId, 2026)).GetProperty("threshold");

        (threshold.GetProperty("peak").GetProperty("total").GetDecimal(), threshold.GetProperty("outcome").GetString(),
                threshold.GetProperty("complete").GetBoolean())
            .ShouldBe((10_000.00m, "NotAbove", true));
        threshold.GetRawText().ShouldNotContain(mistake.ToString());
    }

    private static (string?, decimal?, decimal?) Cost(JsonElement holding)
    {
        var cost = holding.GetProperty("cost");
        return (cost.GetProperty("status").GetString(), Number(cost.GetProperty("opening")), Number(cost.GetProperty("closing")));
    }

    private static (decimal?, decimal?, bool) EventCost(JsonElement taxEvent)
    {
        var cost = taxEvent.GetProperty("cost");
        return (Number(cost.GetProperty("change")), Number(cost.GetProperty("after")), cost.GetProperty("fx").ValueKind != JsonValueKind.Null);
    }

    private static decimal? Number(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
}
