using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Valuations;
using MyWealthV2.Domain.Tax;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>Tax-year-events R3–R32 through <c>GET /users/customers/{customerId}/tax-years/{taxYear}</c>.</summary>
[Parallelizable]
public class GetCustomerTaxYearTests : TestBase
{
    [Test]
    public async Task WorkedExample_2026_IsTheItemOfTheSpec()
    {
        var world = await TaxYearWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow;

        var year = await ReadAsync(world.Token, world.Customer.PublicId, 2026);

        year.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["customerId", "taxYear", "from", "to", "currency", "ledgerReadAt", "holdings", "complete", "incompleteCount", "threshold"]);
        year.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        year.GetProperty("taxYear").GetInt32().ShouldBe(2026);
        year.GetProperty("from").GetString().ShouldBe("2025-03-31T11:00:00Z");
        year.GetProperty("to").GetString().ShouldBe("2026-03-31T11:00:00Z");
        year.GetProperty("currency").GetString().ShouldBe("NZD");
        year.GetProperty("ledgerReadAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        (year.GetProperty("complete").GetBoolean(), year.GetProperty("incompleteCount").GetInt32()).ShouldBe((true, 0));

        var holding = year.GetProperty("holdings").EnumerateArray().ShouldHaveSingleItem();
        var instrument = holding.GetProperty("instrument");
        (instrument.GetProperty("id").GetGuid(), instrument.GetProperty("symbol").GetString(), instrument.GetProperty("quoteCurrency").GetString())
            .ShouldBe((world.Aaa, "AAA", "USD"));
        var treatment = holding.GetProperty("treatment");
        (treatment.GetProperty("fifTreatment").GetString(), treatment.GetProperty("rowTaxYear").GetInt32(), treatment.GetProperty("issuerCountry").GetString())
            .ShouldBe(("Attributing", 2025, "US"));
        treatment.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);

        var opening = holding.GetProperty("opening");
        opening.GetProperty("at").GetString().ShouldBe("2025-03-31T11:00:00Z");
        opening.GetProperty("valuationDay").GetString().ShouldBe("2025-03-31");
        (opening.GetProperty("quantity").GetDecimal(), opening.GetProperty("comparableQuantity").GetDecimal(), opening.GetProperty("value").GetDecimal())
            .ShouldBe((140m, 280m, 13_108.48m));
        opening.GetProperty("status").GetString().ShouldBe("Valued");
        opening.GetProperty("lines").EnumerateArray()
            .Select(line => (line.GetProperty("accountId").GetGuid(), line.GetProperty("custodyMode").GetString(), line.GetProperty("quantity").GetDecimal(),
                line.GetProperty("marketValue").GetDecimal(), line.GetProperty("value").GetDecimal()))
            .ShouldBe([(world.A1, "Custodial", 100m, 5_320.00m, 9_363.20m), (world.A2, "External", 40m, 2_128.00m, 3_745.28m)]);
        var line = opening.GetProperty("lines")[0];
        line.GetProperty("price").GetProperty("amount").GetDecimal().ShouldBe(53.20m);
        line.GetProperty("price").GetProperty("date").GetString().ShouldBe("2025-03-31");
        line.GetProperty("fx").GetProperty("rate").GetDecimal().ShouldBe(1.76m);

        var closing = holding.GetProperty("closing");
        (closing.GetProperty("quantity").GetDecimal(), closing.GetProperty("comparableQuantity").GetDecimal(), closing.GetProperty("value").GetDecimal())
            .ShouldBe((240.8m, 240.8m, 13_026.08m));
        closing.GetProperty("lines").EnumerateArray().ShouldHaveSingleItem().GetProperty("accountId").GetGuid().ShouldBe(world.A1);
        holding.GetProperty("peakQuantity").GetDecimal().ShouldBe(280m);
        (holding.GetProperty("complete").GetBoolean(), Strings(holding.GetProperty("incomplete")).Count).ShouldBe((true, 0));
        Strings(holding.GetProperty("notices")).ShouldBe(["ExternalFigures", "Move"]);

        var events = holding.GetProperty("events").EnumerateArray().ToList();
        events.Select(e => (e.GetProperty("sequence").GetInt32(), e.GetProperty("kind").GetString(), e.GetProperty("transactionId").GetGuid(),
                e.GetProperty("quantityAfter").GetDecimal(), e.GetProperty("comparableQuantityAfter").GetDecimal(), Amount(e)))
            .ShouldBe([
                (1, "Disposal", world.T["T3"], 110m, 220m, 2_789.60m),
                (2, "Dividend", world.T["T4"], 110m, 220m, 59.15m),
                (3, "Reorganisation", world.T["T7"], 150m, 220m, null),
                (4, "Reorganisation", world.T["T8"], 220m, 220m, null),
                (5, "Dividend", world.T["T9"], 220m, 220m, 58.80m),
                (6, "Acquisition", world.T["T9"], 220.8m, 220.8m, 37.63m),
                (7, "Move", world.T["T11"], 220.8m, 220.8m, null),
                (8, "Acquisition", world.T["T12"], 240.8m, 240.8m, 1_046.65m)
            ]);

        var disposal = events[0];
        disposal.EnumerateObject().Select(property => property.Name).ShouldBe([
            "sequence", "kind", "bookedAt", "transactionId", "accountId", "custodyMode", "quantity", "quantityAfter",
            "comparableQuantityAfter", "amount", "status", "ledger", "fx", "price", "principal", "fee", "dividend", "carriedCost", "move", "cost"
        ]);
        disposal.GetProperty("bookedAt").GetString().ShouldBe("2025-03-31T20:15:00Z");
        (disposal.GetProperty("accountId").GetGuid(), disposal.GetProperty("custodyMode").GetString(), disposal.GetProperty("quantity").GetDecimal())
            .ShouldBe((world.A1, "Custodial", -30m));
        disposal.GetProperty("status").GetString().ShouldBe("Converted");
        var ledger = disposal.GetProperty("ledger");
        (ledger.GetProperty("currency").GetString(), ledger.GetProperty("principal").GetDecimal(), ledger.GetProperty("fee").GetDecimal())
            .ShouldBe(("USD", 1_590.00m, 5.00m));
        (disposal.GetProperty("fx").GetProperty("rate").GetDecimal(), disposal.GetProperty("fx").GetProperty("date").GetString())
            .ShouldBe((1.76m, "2025-03-31"));
        (disposal.GetProperty("principal").GetDecimal(), disposal.GetProperty("fee").GetDecimal()).ShouldBe((2_798.40m, 8.80m));
        foreach (var field in new[] { "price", "dividend", "carriedCost", "move" })
        {
            disposal.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }

        var dividend = events[1].GetProperty("dividend");
        (dividend.GetProperty("shares").GetDecimal(), dividend.GetProperty("taxSource").GetString(), dividend.GetProperty("gross").GetDecimal(),
                dividend.GetProperty("foreignTax").GetDecimal(), dividend.GetProperty("rwt").GetDecimal(), dividend.GetProperty("nrwt").GetDecimal(),
                dividend.GetProperty("imputationCredit").GetDecimal())
            .ShouldBe((70m, "Foreign", 59.15m, 8.87m, 10.65m, 0m, 0m));
        var dividendLedger = events[1].GetProperty("ledger");
        (dividendLedger.GetProperty("gross").GetDecimal(), dividendLedger.GetProperty("foreignTax").GetDecimal(), dividendLedger.GetProperty("rwt").GetDecimal())
            .ShouldBe((35.00m, 5.25m, 6.30m));

        var reorganisation = events[2];
        (reorganisation.GetProperty("status").GetString(), reorganisation.GetProperty("custodyMode").GetString()).ShouldBe(("None", "External"));
        reorganisation.GetProperty("ledger").ValueKind.ShouldBe(JsonValueKind.Null);

        var move = events[6];
        move.GetProperty("bookedAt").GetString().ShouldBe("2025-11-14T03:20:00Z");
        var legs = move.GetProperty("move");
        (legs.GetProperty("quantity").GetDecimal(), legs.GetProperty("fromAccountId").GetGuid(), legs.GetProperty("fromTransactionId").GetGuid(),
                legs.GetProperty("fromBookedAt").GetString(), legs.GetProperty("toAccountId").GetGuid(), legs.GetProperty("toTransactionId").GetGuid(),
                legs.GetProperty("toBookedAt").GetString())
            .ShouldBe((80m, world.A2, world.T["T10"], "2025-11-10T00:00:00Z", world.A1, world.T["T11"], "2025-11-14T03:20:00Z"));
    }

    [Test]
    public async Task WorkedExample_2025_ClosesWithTheOpeningOf2026_AndTheReversalIsLeftOut()
    {
        var world = await TaxYearWorld.CreateAsync();

        var year2025 = await ReadAsync(world.Token, world.Customer.PublicId, 2025);
        var year2026 = await ReadAsync(world.Token, world.Customer.PublicId, 2026);

        var holding = year2025.GetProperty("holdings").EnumerateArray().ShouldHaveSingleItem();
        holding.GetProperty("events").EnumerateArray()
            .Select(e => (e.GetProperty("transactionId").GetGuid(), e.GetProperty("status").GetString(), Amount(e)))
            .ShouldBe([(world.T["T1"], "Converted", 8_708.70m), (world.T["T2"], "Stated", 3_647.00m)]);
        var closing = holding.GetProperty("closing");
        var opening = HoldingOf(year2026, "AAA").GetProperty("opening");
        // R22: the same instant, day, rows and value. The comparable quantity is on the basis of each year's end (R24).
        foreach (var field in new[] { "at", "valuationDay", "quantity", "value", "status", "lines" })
        {
            closing.GetProperty(field).GetRawText().ShouldBe(opening.GetProperty(field).GetRawText(), field);
        }

        (closing.GetProperty("comparableQuantity").GetDecimal(), opening.GetProperty("comparableQuantity").GetDecimal()).ShouldBe((140m, 280m));
        Strings(holding.GetProperty("notices")).ShouldBe(["ExternalFigures"]);
        var transactions = year2026.GetProperty("holdings").EnumerateArray()
            .SelectMany(h => h.GetProperty("events").EnumerateArray()).Select(e => e.GetProperty("transactionId").GetGuid()).ToList();
        transactions.ShouldNotContain(world.T["T5"]);
        transactions.ShouldNotContain(world.T["T6"]);
    }

    [Test]
    public async Task ATradeAt1059AndOneAt1100UtcOn31March_FallInTwoTaxYears_WhileBothAreInOneValuationDay()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var kiwi = await CustodialHttp.CreateInstrumentAsync(token, "KIWI", "NZD", price: null);
        await ClassifyAsync(token, kiwi, "NZ", 2025, "NotForeign");
        await PostAsync(token, Cash(account, "TransferIn", 1_000.00m, "2025-03-01T00:00:00Z"));
        var before = await PostAsync(token, Trade(account, "Buy", kiwi, 10m, 2.00m, "2025-03-31T10:59:00Z"));
        var after = await PostAsync(token, Trade(account, "Buy", kiwi, 5m, 2.00m, "2025-03-31T11:00:00Z"));
        await AddPriceAsync(kiwi, "NZD", new DateOnly(2025, 3, 31), 2.10m);

        var year2025 = HoldingOf(await ReadAsync(token, customer.PublicId, 2025), "KIWI");
        var year2026 = HoldingOf(await ReadAsync(token, customer.PublicId, 2026), "KIWI");
        var valuation = Holding(await ValuationHttp.ReadAsync(token, account, new DateOnly(2025, 3, 31)), "KIWI");

        year2025.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("transactionId").GetGuid()).ShouldBe([before]);
        year2026.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("transactionId").GetGuid()).ShouldBe([after]);
        (year2026.GetProperty("opening").GetProperty("quantity").GetDecimal(), year2026.GetProperty("opening").GetProperty("status").GetString())
            .ShouldBe((10m, "NotRequired"));
        valuation.GetProperty("ledgerQuantity").GetDecimal().ShouldBe(15m);
    }

    [Test]
    public async Task AnArrivalAndADeparture_AreAtTheMarketValueOfTheirDay()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var aaa = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "USD", price: null);
        await ClassifyAsync(token, aaa, "US", 2025, "Attributing");
        var arrival = await PostAsync(token, Opening(account, "2025-08-04T00:00:00Z", (aaa, 25m, 700.00m)));
        var departure = await PostAsync(token, Security(account, "StockOut", aaa, 10m, "2025-09-01T00:00:00Z"));
        await AddPriceAsync(aaa, "USD", new DateOnly(2025, 8, 4), 31.20m);
        await AddPriceAsync(aaa, "USD", new DateOnly(2025, 9, 1), 30.00m);
        await AddRateAsync("USD", "NZD", new DateOnly(2025, 8, 4), 1.6800m);
        await AddRateAsync("USD", "NZD", new DateOnly(2025, 9, 1), 1.7000m);

        var holding = HoldingOf(await ReadAsync(token, customer.PublicId, 2026), "AAA");

        var events = holding.GetProperty("events").EnumerateArray().ToList();
        events.Select(e => (e.GetProperty("transactionId").GetGuid(), e.GetProperty("kind").GetString(), e.GetProperty("quantity").GetDecimal(),
                e.GetProperty("status").GetString(), Amount(e)))
            .ShouldBe([(arrival, "Arrival", 25m, "MarketValue", 1_310.40m), (departure, "Departure", -10m, "MarketValue", 510.00m)]);
        var first = events[0];
        first.GetProperty("ledger").ValueKind.ShouldBe(JsonValueKind.Null);
        (first.GetProperty("price").GetProperty("amount").GetDecimal(), first.GetProperty("price").GetProperty("date").GetString())
            .ShouldBe((31.20m, "2025-08-04"));
        first.GetProperty("fx").GetProperty("rate").GetDecimal().ShouldBe(1.68m);
        var cost = first.GetProperty("carriedCost");
        (cost.GetProperty("amount").GetDecimal(), cost.GetProperty("currency").GetString()).ShouldBe((700.00m, "USD"));
        cost.GetProperty("acquiredOn").ValueKind.ShouldBe(JsonValueKind.Null);
        // Tax-cost-threshold R9, R25: the carried cost at the rate of the day it was booked, and its notice.
        (first.GetProperty("cost").GetProperty("change").GetDecimal(), first.GetProperty("cost").GetProperty("fx").GetProperty("rate").GetDecimal())
            .ShouldBe((1_176.00m, 1.68m));
        Strings(holding.GetProperty("notices")).ShouldBe(["ExternalFigures", "ArrivalAtMarketValue", "DepartureAtMarketValue", "CarriedCost"]);
        // No price for 31 March 2026 is stored: the closing is not valued and the holding is incomplete.
        Strings(holding.GetProperty("incomplete")).ShouldBe(["ClosingNotValued"]);
    }

    [Test]
    public async Task AnEntryBackdatedOnTheExternalAccount_ChangesTheNextRead()
    {
        var world = await TaxYearWorld.CreateAsync();
        var first = HoldingOf(await ReadAsync(world.Token, world.Customer.PublicId, 2026), "AAA");

        var backdated = await PostAsync(world.Token, Trade(world.A2, "Buy", world.Aaa, 1m, 54.00m, "2025-04-15T00:00:00Z", fxRate: 1.7500m));
        var second = HoldingOf(await ReadAsync(world.Token, world.Customer.PublicId, 2026), "AAA");

        first.GetProperty("events").GetArrayLength().ShouldBe(8);
        var events = second.GetProperty("events").EnumerateArray().ToList();
        events.Count.ShouldBe(9);
        (events[1].GetProperty("transactionId").GetGuid(), events[1].GetProperty("status").GetString(), Amount(events[1]))
            .ShouldBe((backdated, "Stated", 94.50m));
        second.GetProperty("closing").GetProperty("quantity").GetDecimal().ShouldBe(241.8m);
    }

    [Test]
    public async Task AClosedAccount_StillCounts()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Old broker", "Brokerage", "NZD");
        var kiwi = await CustodialHttp.CreateInstrumentAsync(token, "KIWI", "NZD", price: null);
        await PostAsync(token, Cash(account, "TransferIn", 100.00m, "2025-05-01T00:00:00Z"));
        var buy = await PostAsync(token, Trade(account, "Buy", kiwi, 10m, 2.00m, "2025-05-02T00:00:00Z"));
        var sell = await PostAsync(token, Trade(account, "Sell", kiwi, 10m, 2.50m, "2025-06-02T00:00:00Z"));
        await PostAsync(token, Cash(account, "TransferOut", -105.00m, "2025-06-03T00:00:00Z"));
        await CloseAccountAsync(account);

        var holding = HoldingOf(await ReadAsync(token, customer.PublicId, 2026), "KIWI");

        holding.GetProperty("events").EnumerateArray().Select(e => (e.GetProperty("transactionId").GetGuid(), Amount(e)))
            .ShouldBe([(buy, 20.00m), (sell, 25.00m)]);
        Strings(holding.GetProperty("incomplete")).ShouldBe(["NotClassified"]);
    }

    [Test]
    public async Task ACustomerWithNothing_AnswersAnEmptyListAndComplete()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();

        var year = await ReadAsync(tokens.AccessToken, customer.PublicId, 2026);

        year.GetProperty("holdings").GetArrayLength().ShouldBe(0);
        (year.GetProperty("complete").GetBoolean(), year.GetProperty("incompleteCount").GetInt32()).ShouldBe((true, 0));
    }

    [TestCase("abc", "TaxYear must be a whole number.")]
    [TestCase("2026.5", "TaxYear must be a whole number.")]
    [TestCase("2008", "TaxYear must be 2009 or later.")]
    public async Task ATaxYearThatCannotBeRead_Is400(string taxYear, string message)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();

        var errors = await CustodialHttp.ErrorsAsync(await Send(tokens.AccessToken, customer.PublicId, taxYear));

        errors.Keys.ShouldBe(["TaxYear"]);
        errors["TaxYear"].ShouldBe([message]);
    }

    [Test]
    public async Task TheTaxYearInProgress_Is400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var inProgress = TaxYear.Of(DateTimeOffset.UtcNow);

        var errors = await CustodialHttp.ErrorsAsync(await Send(tokens.AccessToken, customer.PublicId, inProgress.ToString()));

        errors["TaxYear"].ShouldBe([$"Tax year {inProgress} can be read from 2 April {inProgress}."]);
    }

    private static decimal? Amount(JsonElement taxEvent) =>
        taxEvent.GetProperty("amount").ValueKind == JsonValueKind.Null ? null : taxEvent.GetProperty("amount").GetDecimal();
}
