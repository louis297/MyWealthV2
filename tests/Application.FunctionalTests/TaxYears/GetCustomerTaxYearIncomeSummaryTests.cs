using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>
/// Income-summary R1–R14 through <c>GET /users/customers/{customerId}/tax-years/{taxYear}</c>: the fields
/// <c>interest</c> and <c>income</c>, with the worked example of §1.1 and the tenant isolation.
/// </summary>
[Parallelizable]
public class GetCustomerTaxYearIncomeSummaryTests : TestBase
{
    [Test]
    public async Task WorkedExample_TheInterestItemsAreThoseOfTheSpec()
    {
        var world = await IncomeSummaryWorld.CreateAsync();

        var year = await ReadAsync(world.Token, world.CustomerId, 2026);

        var items = year.GetProperty("interest").EnumerateArray().ToList();
        items.Select(i => (i.GetProperty("sequence").GetInt32(), i.GetProperty("transactionId").GetGuid(), i.GetProperty("amount").GetDecimal()))
            .ShouldBe([(1, world.P["I1"], 120.00m), (2, world.P["I2"], -8.00m), (3, world.P["I3"], 7.05m), (4, world.P["I4"], 6.65m)]);

        var i2 = items[1];
        i2.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["sequence", "bookedAt", "transactionId", "accountId", "custodyMode", "amount", "status", "ledger", "fx", "rwt", "nrwt", "foreignTax", "period"]);
        i2.GetProperty("bookedAt").GetString().ShouldBe("2025-10-15T00:00:00Z");
        (i2.GetProperty("accountId").GetGuid(), i2.GetProperty("custodyMode").GetString(), i2.GetProperty("status").GetString())
            .ShouldBe((world.Base.A2, "External", "Stated"));
        Ledger(i2).ShouldBe(("NZD", -8.00m, 0m, 0m, 0m));
        (i2.GetProperty("fx").ValueKind, i2.GetProperty("period").ValueKind).ShouldBe((JsonValueKind.Null, JsonValueKind.Null));
        Taxes(i2).ShouldBe((0m, 0m, 0m));

        var i3 = items[2];
        i3.GetProperty("bookedAt").GetString().ShouldBe("2026-02-01T00:30:00Z");
        (i3.GetProperty("accountId").GetGuid(), i3.GetProperty("custodyMode").GetString(), i3.GetProperty("status").GetString())
            .ShouldBe((world.Base.A1, "Custodial", "Converted"));
        Ledger(i3).ShouldBe(("USD", 4.10m, 1.35m, 0m, 0m));
        var fx = i3.GetProperty("fx");
        (fx.GetProperty("rate").GetDecimal(), fx.GetProperty("date").GetString(), fx.GetProperty("source").GetString())
            .ShouldBe((1.72m, "2026-01-30", "Frankfurter"));
        Taxes(i3).ShouldBe((2.32m, 0m, 0m));
        var period = i3.GetProperty("period");
        (period.GetProperty("from").GetString(), period.GetProperty("to").GetString(), period.GetProperty("days").GetInt32())
            .ShouldBe(("2026-01-01", "2026-01-31", 31));

        // R4: I5, the interest of March 2026 posted on 1 April 2026, is in tax year 2027, which cannot be read yet.
        items.Select(i => i.GetProperty("transactionId").GetGuid()).ShouldNotContain(world.P["I5"]);
    }

    [Test]
    public async Task WorkedExample_TheIncomeSummaryIsThatOfTheSpec()
    {
        var world = await IncomeSummaryWorld.CreateAsync();

        var income = (await ReadAsync(world.Token, world.CustomerId, 2026)).GetProperty("income");

        var groups = income.GetProperty("dividends").GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("group").GetString()).ShouldBe(["NewZealand", "ForeignExempt", "Attributing", "NotClassified"]);
        income.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["dividends", "interest", "taxWithheld", "imputationCredit", "complete", "incomplete", "notices"]);

        var kiw = groups[0].GetProperty("holdings").EnumerateArray().ShouldHaveSingleItem();
        kiw.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["instrumentId", "issuerCountry", "count", "gross", "imputationCredit", "grossWithImputation", "foreignTax", "rwt", "nrwt"]);
        (kiw.GetProperty("instrumentId").GetGuid(), kiw.GetProperty("issuerCountry").GetString()).ShouldBe((world.Kiw, "NZ"));
        DividendSum(kiw).ShouldBe((1, 720.00m, 280.00m, 1_000.00m, 0m, 50.00m, 0m));
        groups[0].GetProperty("countries").GetArrayLength().ShouldBe(0);
        DividendSum(groups[0].GetProperty("total")).ShouldBe((1, 720.00m, 280.00m, 1_000.00m, 0m, 50.00m, 0m));

        var ozb = groups[1].GetProperty("holdings").EnumerateArray().ShouldHaveSingleItem();
        (ozb.GetProperty("instrumentId").GetGuid(), ozb.GetProperty("issuerCountry").GetString()).ShouldBe((world.Ozb, "AU"));
        DividendSum(ozb).ShouldBe((1, 327.00m, 0m, 327.00m, 49.05m, 0m, 0m));
        var au = groups[1].GetProperty("countries").EnumerateArray().ShouldHaveSingleItem();
        au.GetProperty("issuerCountry").GetString().ShouldBe("AU");
        DividendSum(au).ShouldBe((1, 327.00m, 0m, 327.00m, 49.05m, 0m, 0m));

        var aaa = groups[2].GetProperty("holdings").EnumerateArray().ShouldHaveSingleItem();
        aaa.GetProperty("instrumentId").GetGuid().ShouldBe(world.Base.Aaa);
        DividendSum(aaa).ShouldBe((2, 117.95m, 0m, 117.95m, 17.69m, 21.23m, 0m));

        groups[3].GetProperty("holdings").GetArrayLength().ShouldBe(0);
        DividendSum(groups[3].GetProperty("total")).ShouldBe((0, 0m, 0m, 0m, 0m, 0m, 0m));
        DividendSum(income.GetProperty("dividends").GetProperty("total")).ShouldBe((4, 1_164.95m, 280.00m, 1_444.95m, 66.74m, 71.23m, 0m));

        var accounts = income.GetProperty("interest").GetProperty("accounts").EnumerateArray().ToList();
        accounts[0].EnumerateObject().Select(p => p.Name).ShouldBe(
            ["accountId", "custodyMode", "currency", "count", "received", "charged", "rwt", "nrwt", "foreignTax"]);
        accounts.Select(a => (a.GetProperty("accountId").GetGuid(), a.GetProperty("custodyMode").GetString(), a.GetProperty("currency").GetString()))
            .ShouldBe([(world.Base.A2, "External", "NZD"), (world.Base.A1, "Custodial", "USD")]);
        InterestSum(accounts[0]).ShouldBe((2, 120.00m, 8.00m, 21.00m, 0m, 0m));
        InterestSum(accounts[1]).ShouldBe((2, 13.70m, 0m, 4.51m, 0m, 0m));
        InterestSum(income.GetProperty("interest").GetProperty("total")).ShouldBe((4, 133.70m, 8.00m, 25.51m, 0m, 0m));

        var taxWithheld = income.GetProperty("taxWithheld");
        (taxWithheld.GetProperty("rwt").GetDecimal(), taxWithheld.GetProperty("nrwt").GetDecimal(), taxWithheld.GetProperty("foreignTax").GetDecimal())
            .ShouldBe((96.74m, 0m, 66.74m));
        income.GetProperty("imputationCredit").GetDecimal().ShouldBe(280.00m);
        income.GetProperty("complete").GetBoolean().ShouldBeTrue();
        income.GetProperty("incomplete").GetArrayLength().ShouldBe(0);
        Notices(income).ShouldBe([("ExternalFigures", null, world.Base.A2), ("InterestCharged", null, world.Base.A2)]);
    }

    [Test]
    public async Task AnInterestEnteredWithAnEarlierBookedAt_ChangesTheNextRead()
    {
        var world = await IncomeSummaryWorld.CreateAsync();
        var first = await ReadAsync(world.Token, world.CustomerId, 2026);

        var earlier = await IncomeSummaryWorld.HandInterestAsync(world.Token, world.Base.A2, 50.00m, "2025-08-01T00:00:00Z");
        var second = await ReadAsync(world.Token, world.CustomerId, 2026);

        first.GetProperty("interest").GetArrayLength().ShouldBe(4);
        var items = second.GetProperty("interest").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("sequence").GetInt32()).ShouldBe([1, 2, 3, 4, 5]);
        items[0].GetProperty("transactionId").GetGuid().ShouldBe(earlier);
        InterestSum(second.GetProperty("income").GetProperty("interest").GetProperty("accounts")[0]).ShouldBe((3, 170.00m, 8.00m, 21.00m, 0m, 0m));
    }

    [Test]
    public async Task AReversedHandInterest_IsInNoItemAndNoSum()
    {
        var world = await IncomeSummaryWorld.CreateAsync();

        await CustodialHttp.ReverseAsync(world.Token, world.P["I1"]);
        var year = await ReadAsync(world.Token, world.CustomerId, 2026);

        year.GetProperty("interest").EnumerateArray().Select(i => i.GetProperty("transactionId").GetGuid())
            .ShouldBe([world.P["I2"], world.P["I3"], world.P["I4"]]);
        InterestSum(year.GetProperty("income").GetProperty("interest").GetProperty("total")).ShouldBe((3, 13.70m, 8.00m, 4.51m, 0m, 0m));
    }

    [Test]
    public async Task AMissingRateOfAnInterestItem_ChangesTheCompleteOfTheIncomeSummary_AndNotThatOfTheTaxYear()
    {
        var world = await IncomeSummaryWorld.CreateAsync();
        var before = await ReadAsync(world.Token, world.CustomerId, 2026);

        // 2025-07-20: the latest USD row is that of 2025-05-20, older than 4 days.
        var stale = await PostInterestAsync(
            world.Base.A1, 2.00m, 33m, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30), IncomeSummaryWorld.Utc(2025, 7, 20, 0, 30));
        var after = await ReadAsync(world.Token, world.CustomerId, 2026);

        after.GetProperty("complete").GetBoolean().ShouldBe(before.GetProperty("complete").GetBoolean());
        after.GetProperty("incompleteCount").GetInt32().ShouldBe(before.GetProperty("incompleteCount").GetInt32());
        after.GetProperty("interest").EnumerateArray().Select(i => i.GetProperty("transactionId").GetGuid()).ShouldContain(stale);
        var item = after.GetProperty("interest").EnumerateArray().Single(i => i.GetProperty("transactionId").GetGuid() == stale);
        (item.GetProperty("status").GetString(), item.GetProperty("amount").ValueKind).ShouldBe(("StaleRate", JsonValueKind.Null));
        var income = after.GetProperty("income");
        income.GetProperty("complete").GetBoolean().ShouldBeFalse();
        income.GetProperty("incomplete").EnumerateArray()
            .Select(r => (r.GetProperty("reason").GetString(), r.GetProperty("accountId").GetGuid()))
            .ShouldBe([("AmountMissing", world.Base.A1)]);
        income.GetProperty("interest").GetProperty("total").GetProperty("received").ValueKind.ShouldBe(JsonValueKind.Null);
        income.GetProperty("interest").GetProperty("total").GetProperty("count").GetInt32().ShouldBe(5);
        income.GetProperty("taxWithheld").GetProperty("rwt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TwoTenants_NoTransactionOfTheOtherTenantIsInAnyItemOrSum_AndTheOtherTenantsCustomerIs404()
    {
        var north = await IncomeSummaryWorld.CreateAsync();
        var south = await IncomeSummaryWorld.CreateAsync("South Advisory", "south-advisory", "south.example");
        await IncomeSummaryWorld.HandInterestAsync(south.Token, south.Base.A2, 999.00m, "2025-08-01T00:00:00Z");

        var year = await ReadAsync(north.Token, north.CustomerId, 2026);

        var text = year.GetRawText();
        foreach (var id in south.P.Values.Append(south.Base.A1).Append(south.Base.A2).Append(south.Kiw).Append(south.Ozb))
        {
            text.ShouldNotContain(id.ToString());
        }

        year.GetProperty("interest").EnumerateArray().Select(i => i.GetProperty("transactionId").GetGuid())
            .ShouldAllBe(id => north.P.Values.Contains(id));
        InterestSum(year.GetProperty("income").GetProperty("interest").GetProperty("total")).ShouldBe((4, 133.70m, 8.00m, 25.51m, 0m, 0m));
        DividendSum(year.GetProperty("income").GetProperty("dividends").GetProperty("total")).ShouldBe((4, 1_164.95m, 280.00m, 1_444.95m, 66.74m, 71.23m, 0m));
        (await Send(north.Token, south.CustomerId, "2026")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static (string?, decimal, decimal, decimal, decimal) Ledger(JsonElement item)
    {
        var ledger = item.GetProperty("ledger");
        return (ledger.GetProperty("currency").GetString(), ledger.GetProperty("gross").GetDecimal(), ledger.GetProperty("rwt").GetDecimal(),
            ledger.GetProperty("nrwt").GetDecimal(), ledger.GetProperty("foreignTax").GetDecimal());
    }

    private static (decimal, decimal, decimal) Taxes(JsonElement item) =>
        (item.GetProperty("rwt").GetDecimal(), item.GetProperty("nrwt").GetDecimal(), item.GetProperty("foreignTax").GetDecimal());

    private static (int, decimal, decimal, decimal, decimal, decimal, decimal) DividendSum(JsonElement sum) =>
        (sum.GetProperty("count").GetInt32(), sum.GetProperty("gross").GetDecimal(), sum.GetProperty("imputationCredit").GetDecimal(),
            sum.GetProperty("grossWithImputation").GetDecimal(), sum.GetProperty("foreignTax").GetDecimal(), sum.GetProperty("rwt").GetDecimal(),
            sum.GetProperty("nrwt").GetDecimal());

    private static (int, decimal, decimal, decimal, decimal, decimal) InterestSum(JsonElement sum) =>
        (sum.GetProperty("count").GetInt32(), sum.GetProperty("received").GetDecimal(), sum.GetProperty("charged").GetDecimal(),
            sum.GetProperty("rwt").GetDecimal(), sum.GetProperty("nrwt").GetDecimal(), sum.GetProperty("foreignTax").GetDecimal());

    private static List<(string?, Guid?, Guid?)> Notices(JsonElement income) =>
        income.GetProperty("notices").EnumerateArray()
            .Select(n => (n.GetProperty("notice").GetString(),
                n.GetProperty("instrumentId").ValueKind == JsonValueKind.Null ? (Guid?)null : n.GetProperty("instrumentId").GetGuid(),
                n.GetProperty("accountId").ValueKind == JsonValueKind.Null ? (Guid?)null : n.GetProperty("accountId").GetGuid()))
            .ToList();
}
