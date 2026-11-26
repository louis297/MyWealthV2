using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationWorld;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Account valuation R1–R17, R20–R22: <c>GET /accounts/{id}/valuation?date=</c> on an External account.</summary>
[Parallelizable]
public class GetAccountValuationTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await GetAsync(null, Guid.NewGuid(), "2026-03-05");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    /// <summary>§1.1 day by day: the cash, each quantity, each value and the totals.</summary>
    [Test]
    public async Task TheWorkedExample_DayByDay()
    {
        var world = await CreateAsync();
        var days = new (int Day, decimal Cash, decimal Aaa, decimal Bbb, decimal Ccc, decimal? CccValue, decimal Total, bool Complete)[]
        {
            (2, 10_000.00m, 1_000m, 50m, 200m, 13_680.00m, 41_550.00m, true),
            (3, 8_735.00m, 1_500m, 50m, 200m, 13_891.50m, 41_983.00m, true),
            (4, 10_735.00m, 1_500m, 50m, 200m, 13_812.72m, 44_133.32m, true),
            (5, 16_356.60m, 1_500m, 100m, 120m, 8_462.40m, 44_434.00m, true),
            (6, 14_356.60m, 1_500m, 100m, 120m, 8_549.10m, 42_790.45m, true),
            (7, 14_356.60m, 1_500m, 100m, 120m, 8_549.10m, 42_790.45m, true),
            (8, 14_356.60m, 1_500m, 100m, 120m, 8_554.06m, 42_804.72m, true),
            (9, 14_356.60m, 1_500m, 100m, 120m, 8_573.88m, 43_092.48m, true),
            (10, 14_356.60m, 1_500m, 100m, 120m, 8_563.97m, 42_930.09m, true),
            (11, 14_356.60m, 1_500m, 100m, 120m, null, 34_750.72m, false)
        };

        foreach (var expected in days)
        {
            var valuation = await ReadAsync(world.Token, world.AccountId, Day(expected.Day));
            var because = $"March {expected.Day}";
            valuation.GetProperty("cash").GetDecimal().ShouldBe(expected.Cash, because);
            valuation.GetProperty("holdings").EnumerateArray().Select(line => (line.GetProperty("symbol").GetString(), line.GetProperty("quantity").GetDecimal()))
                .ShouldBe([("AAA", expected.Aaa), ("BBB", expected.Bbb), ("CCC", expected.Ccc)], because);
            var ccc = Holding(valuation, "CCC").GetProperty("value");
            (ccc.ValueKind == JsonValueKind.Null ? (decimal?)null : ccc.GetDecimal()).ShouldBe(expected.CccValue, because);
            valuation.GetProperty("total").GetDecimal().ShouldBe(expected.Total, because);
            valuation.GetProperty("complete").GetBoolean().ShouldBe(expected.Complete, because);
            valuation.GetProperty("unvaluedCount").GetInt32().ShouldBe(expected.Complete ? 0 : 1, because);
        }
    }

    [Test]
    public async Task Thursday_HasTheItemOfR21_SortedBySymbol()
    {
        var world = await CreateAsync();

        var valuation = await ReadAsync(world.Token, world.AccountId, Day(5));

        valuation.EnumerateObject().Select(property => property.Name).ShouldBe([
            "accountId", "date", "currency", "cash", "holdings", "holdingsValue", "total", "complete", "unvaluedCount"
        ]);
        valuation.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        valuation.GetProperty("date").GetString().ShouldBe("2026-03-05");
        valuation.GetProperty("currency").GetString().ShouldBe("NZD");
        valuation.GetProperty("holdingsValue").GetDecimal().ShouldBe(28_077.40m);

        var aaa = Holding(valuation, "AAA");
        aaa.EnumerateObject().Select(property => property.Name).ShouldBe([
            "instrumentId", "symbol", "name", "quantity", "ledgerQuantity", "status", "price", "marketValue", "fx", "value"
        ]);
        aaa.GetProperty("instrumentId").GetGuid().ShouldBe(world.Aaa);
        aaa.GetProperty("name").GetString().ShouldBe("AAA fund");
        aaa.GetProperty("status").GetString().ShouldBe("Valued");
        AssertPrice(aaa, 2.47m, "NZD", "2026-03-05");
        aaa.GetProperty("marketValue").GetDecimal().ShouldBe(3_705.00m);
        aaa.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
        aaa.GetProperty("value").GetDecimal().ShouldBe(3_705.00m);

        var bbb = Holding(valuation, "BBB");
        (bbb.GetProperty("quantity").GetDecimal(), bbb.GetProperty("ledgerQuantity").GetDecimal()).ShouldBe((100m, 100m));
        AssertPrice(bbb, 92.50m, "USD", "2026-03-05");
        bbb.GetProperty("marketValue").GetDecimal().ShouldBe(9_250.00m);
        var fx = bbb.GetProperty("fx");
        fx.EnumerateObject().Select(property => property.Name).ShouldBe(["rate", "date", "source"]);
        (fx.GetProperty("rate").GetDecimal(), fx.GetProperty("date").GetString(), fx.GetProperty("source").GetString())
            .ShouldBe((1.7200m, "2026-03-05", "Frankfurter"));
        bbb.GetProperty("value").GetDecimal().ShouldBe(15_910.00m);
    }

    [Test]
    public async Task Wednesday11_CccIsAStalePrice_AndTheTotalLeavesItOut()
    {
        var world = await CreateAsync();

        var valuation = await ReadAsync(world.Token, world.AccountId, Day(11));

        var ccc = Holding(valuation, "CCC");
        ccc.GetProperty("status").GetString().ShouldBe("StalePrice");
        AssertPrice(ccc, 41.30m, "USD", "2026-03-06");
        ccc.GetProperty("marketValue").ValueKind.ShouldBe(JsonValueKind.Null);
        ccc.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
        ccc.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        valuation.GetProperty("holdingsValue").GetDecimal().ShouldBe(20_394.12m);
        valuation.GetProperty("total").GetDecimal().ShouldBe(34_750.72m);
    }

    [Test]
    public async Task TheWeekend_UsesFridaysPrice_AndEachDaysRate()
    {
        var world = await CreateAsync();

        foreach (var (day, rateDate) in new[] { (7, "2026-03-07"), (8, "2026-03-08") })
        {
            var bbb = Holding(await ReadAsync(world.Token, world.AccountId, Day(day)), "BBB");
            bbb.GetProperty("price").GetProperty("date").GetString().ShouldBe("2026-03-06");
            bbb.GetProperty("fx").GetProperty("date").GetString().ShouldBe(rateDate);
        }
    }

    [Test]
    public async Task AReversal_CountsFromItsOwnBookedAt()
    {
        var world = await CreateAsync();

        // T2 (+2,000.00) is in Wednesday and Thursday; its Reversal T4 is booked on Friday.
        (await CashAsync(world, Day(4))).ShouldBe(10_735.00m);
        (await CashAsync(world, Day(5))).ShouldBe(16_356.60m);
        (await CashAsync(world, Day(6))).ShouldBe(14_356.60m);
    }

    [Test]
    public async Task ABookedAtWithAnOffset_CountsInItsUtcDay()
    {
        var world = await CreateAsync();

        // T1 was entered as 2026-03-04T09:30:00+13:00, which is Tuesday 3 March 20:30 UTC.
        Holding(await ReadAsync(world.Token, world.AccountId, Day(3)), "AAA").GetProperty("quantity").GetDecimal()
            .ShouldBe(1_500m);
        Holding(await ReadAsync(world.Token, world.AccountId, Day(2)), "AAA").GetProperty("quantity").GetDecimal()
            .ShouldBe(1_000m);
    }

    [Test]
    public async Task ATransactionAtTheLastSecond_IsInTheDay_AndOneAtMidnight_IsInTheNext()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        await PostAsync(tokens.AccessToken, Cash(accountId, "TransferIn", 1.00m, "2026-03-05T23:59:59Z"));
        await PostAsync(tokens.AccessToken, Cash(accountId, "TransferIn", 2.00m, "2026-03-06T00:00:00Z"));

        (await ReadAsync(tokens.AccessToken, accountId, Day(5))).GetProperty("cash").GetDecimal().ShouldBe(1.00m);
        (await ReadAsync(tokens.AccessToken, accountId, Day(6))).GetProperty("cash").GetDecimal().ShouldBe(3.00m);
    }

    [Test]
    public async Task AnInstrumentSoldToZero_IsLeftOut()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var accountId = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var instrument = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "NZD", price: null);
        await PostAsync(token, Opening(accountId, "2026-03-02T09:00:00Z", (instrument, 10m, 20.00m)));
        await PostAsync(token, Trade(accountId, "Sell", instrument, 10m, 2.50m, "2026-03-04T09:00:00Z"));
        await AddPriceAsync(instrument, "NZD", Day(3), 2.50m);

        (await ReadAsync(token, accountId, Day(3))).GetProperty("holdings").GetArrayLength().ShouldBe(1);
        var after = await ReadAsync(token, accountId, Day(4));
        after.GetProperty("holdings").GetArrayLength().ShouldBe(0);
        (after.GetProperty("cash").GetDecimal(), after.GetProperty("complete").GetBoolean()).ShouldBe((25.00m, true));
    }

    [Test]
    public async Task NoPriceNoRateAndAStaleRate_EachLeaveTheLineOut_AndTheValuationIncomplete()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var accountId = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var priced = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "NZD", price: null);
        var unpriced = await CustodialHttp.CreateInstrumentAsync(token, "NOP", "NZD", price: null);
        var noRate = await CustodialHttp.CreateInstrumentAsync(token, "EUR1", "EUR", price: null);
        var staleRate = await CustodialHttp.CreateInstrumentAsync(token, "GBP1", "GBP", price: null);
        await PostAsync(token, Cash(accountId, "TransferIn", 100.00m, "2026-03-02T09:00:00Z"));
        await PostAsync(token, Opening(accountId, "2026-03-02T09:00:00Z",
            (priced, 10m, 10m), (unpriced, 10m, 10m), (noRate, 10m, 10m), (staleRate, 10m, 10m)));
        foreach (var (instrument, currency) in new[] { (priced, "NZD"), (noRate, "EUR"), (staleRate, "GBP") })
        {
            await AddPriceAsync(instrument, currency, Day(10), 2.00m);
        }

        await AddRateAsync("GBP", "NZD", Day(5), 2.10m);

        var valuation = await ReadAsync(token, accountId, Day(10));

        valuation.GetProperty("holdings").EnumerateArray()
            .Select(line => (line.GetProperty("symbol").GetString(), line.GetProperty("status").GetString()))
            .ShouldBe([("AAA", "Valued"), ("EUR1", "NoRate"), ("GBP1", "StaleRate"), ("NOP", "NoPrice")]);
        var eur = Holding(valuation, "EUR1");
        (eur.GetProperty("marketValue").GetDecimal(), eur.GetProperty("fx").ValueKind, eur.GetProperty("value").ValueKind)
            .ShouldBe((20.00m, JsonValueKind.Null, JsonValueKind.Null));
        var gbp = Holding(valuation, "GBP1");
        gbp.GetProperty("fx").GetProperty("date").GetString().ShouldBe("2026-03-05");
        gbp.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        Holding(valuation, "NOP").GetProperty("price").ValueKind.ShouldBe(JsonValueKind.Null);
        (valuation.GetProperty("holdingsValue").GetDecimal(), valuation.GetProperty("total").GetDecimal()).ShouldBe((20.00m, 120.00m));
        (valuation.GetProperty("complete").GetBoolean(), valuation.GetProperty("unvaluedCount").GetInt32()).ShouldBe((false, 3));
    }

    [Test]
    public async Task ADayBeforeTheFirstTransaction_HasNoCashAndNoHolding_AndIsComplete()
    {
        var world = await CreateAsync();

        var valuation = await ReadAsync(world.Token, world.AccountId, new DateOnly(2026, 2, 26));

        valuation.GetProperty("cash").GetDecimal().ShouldBe(0m);
        valuation.GetProperty("holdings").GetArrayLength().ShouldBe(0);
        (valuation.GetProperty("total").GetDecimal(), valuation.GetProperty("complete").GetBoolean()).ShouldBe((0m, true));
    }

    [Test]
    public async Task Today_IsAllowed_AndAnswersThePositionSoFar()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        await PostAsync(tokens.AccessToken, Cash(accountId, "TransferIn", 5.00m, DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")));

        var valuation = await ReadAsync(tokens.AccessToken, accountId, Today);

        valuation.GetProperty("date").GetString().ShouldBe(Today.ToString("yyyy-MM-dd"));
        valuation.GetProperty("cash").GetDecimal().ShouldBe(5.00m);
    }

    [Test]
    public async Task AClosedAccount_IsValuedForADayItWasOpen()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var accountId = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        await PostAsync(token, Cash(accountId, "TransferIn", 50.00m, "2026-03-02T09:00:00Z"));
        await PostAsync(token, Cash(accountId, "TransferOut", -50.00m, "2026-03-09T09:00:00Z"));
        (await CustodialHttp.CloseAsync(token, accountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReadAsync(token, accountId, Day(5))).GetProperty("cash").GetDecimal().ShouldBe(50.00m);
        var after = await ReadAsync(token, accountId, Day(10));
        (after.GetProperty("cash").GetDecimal(), after.GetProperty("holdings").GetArrayLength()).ShouldBe((0m, 0));
    }

    [Test]
    public async Task AnEntryWithAnEarlierBookedAt_ChangesThatPastDay()
    {
        var world = await CreateAsync();
        (await CashAsync(world, Day(3))).ShouldBe(8_735.00m);

        await PostAsync(world.Token, Cash(world.AccountId, "TransferIn", 100.00m, "2026-03-03T08:00:00Z"));

        (await CashAsync(world, Day(3))).ShouldBe(8_835.00m);
        (await CashAsync(world, Day(2))).ShouldBe(10_000.00m);
    }

    [TestCase(null, "Date is required.")]
    [TestCase("", "Date is required.")]
    [TestCase("2026-13-01", "Date must be a date as yyyy-MM-dd.")]
    [TestCase("05/03/2026", "Date must be a date as yyyy-MM-dd.")]
    [TestCase("2026-03-05T00:00:00Z", "Date must be a date as yyyy-MM-dd.")]
    public async Task AMissingOrMalformedDate_Returns400(string? date, string message)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");

        var errors = await CustodialHttp.ErrorsAsync(await GetAsync(tokens.AccessToken, accountId, date));

        errors["Date"].ShouldBe([message]);
    }

    [Test]
    public async Task ADateAfterToday_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");

        var errors = await CustodialHttp.ErrorsAsync(
            await GetAsync(tokens.AccessToken, accountId, Today.AddDays(1).ToString("yyyy-MM-dd")));

        errors["Date"].ShouldBe(["A valuation can be read only for today or a past day."]);
    }

    private static async Task<decimal> CashAsync(ValuationWorld world, DateOnly day) =>
        (await ReadAsync(world.Token, world.AccountId, day)).GetProperty("cash").GetDecimal();

    private static void AssertPrice(JsonElement line, decimal amount, string currency, string date)
    {
        var price = line.GetProperty("price");
        price.EnumerateObject().Select(property => property.Name).ShouldBe(["amount", "currency", "date", "source"]);
        (price.GetProperty("amount").GetDecimal(), price.GetProperty("currency").GetString(),
                price.GetProperty("date").GetString(), price.GetProperty("source").GetString())
            .ShouldBe((amount, currency, date, "EODHD"));
    }
}
