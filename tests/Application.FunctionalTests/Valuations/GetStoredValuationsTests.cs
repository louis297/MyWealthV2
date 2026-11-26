using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Interest;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationRunDb;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationWorld;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Valuation-run R17–R21: the stored valuations of an account and <c>valuedThrough</c> on the account item.</summary>
[Parallelizable]
public class GetStoredValuationsTests : TestBase
{
    private static readonly DateTimeOffset Opened = new(2026, 2, 27, 9, 0, 0, TimeSpan.Zero);

    private static string Series(Guid accountId, string query = "") => $"/accounts/{accountId}/valuations{query}";

    private static string OneDay(Guid accountId, string date) => $"/accounts/{accountId}/valuations/{date}";

    [Test]
    public async Task Series_IsNewestDayFirst_WithTheFieldsOfR17_FiltersAndPages()
    {
        var world = await StoredWorldAsync();
        var token = world.Token;

        var window = await ValuationRunHttp.GetAsync(token, Series(world.AccountId, "?from=2026-03-09&to=2026-03-11"));
        (window.GetProperty("page").GetInt32(), window.GetProperty("pageSize").GetInt32(), window.GetProperty("totalCount").GetInt32())
            .ShouldBe((1, 20, 3));
        var items = window.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("date").GetString()).ShouldBe(["2026-03-11", "2026-03-10", "2026-03-09"]);
        items[0].EnumerateObject().Select(property => property.Name)
            .ShouldBe(["date", "currency", "cash", "holdingsValue", "total", "complete", "unvaluedCount"]);
        (items[0].GetProperty("currency").GetString(), items[0].GetProperty("cash").GetDecimal(),
                items[0].GetProperty("holdingsValue").GetDecimal(), items[0].GetProperty("total").GetDecimal(),
                items[0].GetProperty("complete").GetBoolean(), items[0].GetProperty("unvaluedCount").GetInt32())
            .ShouldBe(("NZD", 14_356.60m, 20_394.12m, 34_750.72m, false, 1));
        (items[1].GetProperty("total").GetDecimal(), items[1].GetProperty("complete").GetBoolean()).ShouldBe((42_930.09m, true));
        items[2].GetProperty("total").GetDecimal().ShouldBe(43_092.48m);

        var all = await ValuationRunHttp.GetAsync(token, Series(world.AccountId));
        all.GetProperty("totalCount").GetInt32().ShouldBe(13);
        all.GetProperty("items").EnumerateArray().First().GetProperty("date").GetString().ShouldBe("2026-03-11");
        Dates(await ValuationRunHttp.GetAsync(token, Series(world.AccountId, "?complete=false")))
            .ShouldBe(["2026-03-11", "2026-03-01", "2026-02-28", "2026-02-27"]);
        (await ValuationRunHttp.GetAsync(token, Series(world.AccountId, "?complete=true"))).GetProperty("totalCount").GetInt32()
            .ShouldBe(9);
        Dates(await ValuationRunHttp.GetAsync(token, Series(world.AccountId, "?pageSize=5&page=3")))
            .ShouldBe(["2026-03-01", "2026-02-28", "2026-02-27"]);

        (await CustodialHttp.ErrorsAsync(await Get(token, Series(world.AccountId, "?from=9-3-2026")))).Keys.ShouldContain("From");
        (await CustodialHttp.ErrorsAsync(await Get(token, Series(world.AccountId, "?to=later")))).Keys.ShouldContain("To");
        (await CustodialHttp.ErrorsAsync(await Get(token, Series(world.AccountId, "?from=2026-03-11&to=2026-03-09"))))
            .Keys.ShouldContain("From");
        (await Get(token, Series(world.AccountId, "?pageSize=101"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task OneDay_IsTheStoredItem_WithTheRunAndWhenItWasStored()
    {
        var world = await StoredWorldAsync();

        var item = await ValuationRunHttp.GetAsync(world.Token, OneDay(world.AccountId, "2026-03-11"));

        item.EnumerateObject().Select(property => property.Name).ShouldBe([
            "accountId", "date", "currency", "cash", "holdings", "holdingsValue", "total", "complete", "unvaluedCount", "runId",
            "valuedAt"
        ]);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("date").GetString().ShouldBe("2026-03-11");
        (item.GetProperty("total").GetDecimal(), item.GetProperty("complete").GetBoolean(), item.GetProperty("unvaluedCount").GetInt32())
            .ShouldBe((34_750.72m, false, 1));
        item.GetProperty("runId").GetGuid().ShouldBe(world.RunId);
        item.GetProperty("valuedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-10));
        item.GetProperty("holdings").EnumerateArray().Select(line => line.GetProperty("symbol").GetString()).ShouldBe(["AAA", "BBB", "CCC"]);
        var ccc = ValuationHttp.Holding(item, "CCC");
        ccc.EnumerateObject().Select(property => property.Name).ShouldBe([
            "instrumentId", "symbol", "name", "quantity", "ledgerQuantity", "status", "price", "marketValue", "fx", "value"
        ]);
        ccc.GetProperty("instrumentId").GetGuid().ShouldBe(world.World.Ccc);
        ccc.GetProperty("status").GetString().ShouldBe("StalePrice");
        (ccc.GetProperty("quantity").GetDecimal(), ccc.GetProperty("ledgerQuantity").GetDecimal()).ShouldBe((120m, 120m));
        var price = ccc.GetProperty("price");
        (price.GetProperty("amount").GetDecimal(), price.GetProperty("currency").GetString(), price.GetProperty("date").GetString(),
                price.GetProperty("source").GetString())
            .ShouldBe((41.30m, "USD", "2026-03-06", "EODHD"));
        ccc.GetProperty("marketValue").ValueKind.ShouldBe(JsonValueKind.Null);
        ccc.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
        ccc.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        var bbb = ValuationHttp.Holding(item, "BBB");
        bbb.GetProperty("fx").GetProperty("rate").GetDecimal().ShouldBe(1.7310m);
        bbb.GetProperty("fx").GetProperty("source").GetString().ShouldBe("Frankfurter");

        (await CustodialHttp.ErrorsAsync(await Get(world.Token, OneDay(world.AccountId, "11-03-2026")))).Keys.ShouldContain("Date");
        (await Get(world.Token, OneDay(world.AccountId, "2026-03-12"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AnExternalAccount_AndACustodialAccountNotValuedYet_AnswerAnEmptyPage()
    {
        var world = await CreateAsync();
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var custodial = await InterestHttp.OpenCustodialAsync(tenant.Id, tokens.AccessToken, customer.PublicId);

        foreach (var (token, accountId) in new[] { (world.Token, world.AccountId), (tokens.AccessToken, custodial) })
        {
            var page = await ValuationRunHttp.GetAsync(token, Series(accountId));
            page.GetProperty("items").GetArrayLength().ShouldBe(0);
            page.GetProperty("totalCount").GetInt32().ShouldBe(0);
            (await Get(token, OneDay(accountId, "2026-03-11"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task TheAccountItem_CarriesValuedThrough_NullOnAnExternalAccount_AndBeforeTheFirstRun()
    {
        var world = await CreateAsync();
        (await AccountClosureHttp.AccountAsync(world.Token, world.AccountId)).GetProperty("valuedThrough").ValueKind
            .ShouldBe(JsonValueKind.Null);
        await MakeCustodialAsync(world.AccountId, Opened);
        (await AccountClosureHttp.AccountAsync(world.Token, world.AccountId)).GetProperty("valuedThrough").ValueKind
            .ShouldBe(JsonValueKind.Null);

        await RunAsync(world.Tenant.Id, Day(11));

        (await AccountClosureHttp.AccountAsync(world.Token, world.AccountId)).GetProperty("valuedThrough").GetString()
            .ShouldBe("2026-03-11");
        var list = await ValuationRunHttp.GetAsync(world.Token, "/accounts");
        list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == world.AccountId)
            .GetProperty("valuedThrough").GetString().ShouldBe("2026-03-11");
    }

    private sealed record Stored(ValuationWorld World, Guid AccountId, string Token, Guid RunId);

    /// <summary>The worked example made Custodial and stored through Wednesday 11 by one run.</summary>
    private static async Task<Stored> StoredWorldAsync()
    {
        var world = await CreateAsync();
        await MakeCustodialAsync(world.AccountId, Opened);
        var run = await RunAsync(world.Tenant.Id, Day(11));
        return new Stored(world, world.AccountId, world.Token, run.PublicId);
    }

    private static Task<HttpResponseMessage> Get(string token, string path) => ValuationRunHttp.Send(HttpMethod.Get, path, token);

    private static List<string?> Dates(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("date").GetString()).ToList();
}
