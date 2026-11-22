using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.MarketData;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionFeedHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R14, R38: <c>GET /corporate-action-announcements</c> and the corporate action feed state on
/// the connection item.</summary>
[Parallelizable]
public class CorporateActionAnnouncementReadTests : TestBase
{
    private const string Path = "/corporate-action-announcements";

    [Test]
    public async Task SystemAdmin_ListsTheAnnouncements_NewestFirst_WithEveryField()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ann");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Item("w-CA1"), Item("w-CA2", kind: null));
        var system = await AccountHttp.SignInSystemAdmin();

        var page = await CorporateActionHttp.GetAsync(system.AccessToken, Path);

        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("outsideId").GetString()).ShouldBe(["w-CA2-V1", "w-CA1-V1"]);
        var skipped = items[0];
        skipped.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "connection", "outsideId", "partyActionId", "version", "partyStatus", "symbol", "currency", "kind", "ratioNew",
            "ratioHeld", "exDate", "recordDate", "paymentDate", "receivedAt", "status", "skipReason", "processedAt"
        ], ignoreOrder: true);
        skipped.GetProperty("kind").ValueKind.ShouldBe(JsonValueKind.Null);
        skipped.GetProperty("status").GetString().ShouldBe("Skipped");
        skipped.GetProperty("skipReason").GetString().ShouldBe("UnsupportedKind");
        var processed = items[1];
        processed.GetProperty("connection").GetString().ShouldBe(Connection);
        processed.GetProperty("partyActionId").GetString().ShouldBe("w-CA1");
        processed.GetProperty("version").GetInt32().ShouldBe(1);
        processed.GetProperty("partyStatus").GetString().ShouldBe("Announced");
        processed.GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        processed.GetProperty("currency").GetString().ShouldBe("USD");
        processed.GetProperty("kind").GetString().ShouldBe("Split");
        (processed.GetProperty("ratioNew").GetInt32(), processed.GetProperty("ratioHeld").GetInt32()).ShouldBe((2, 1));
        processed.GetProperty("exDate").GetString().ShouldBe("2026-11-24");
        processed.GetProperty("status").GetString().ShouldBe("Processed");
        processed.GetProperty("skipReason").ValueKind.ShouldBe(JsonValueKind.Null);
        processed.GetProperty("processedAt").ValueKind.ShouldBe(JsonValueKind.String);
        processed.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task TheFilters_NarrowByConnectionAndStatus_AndAnUnknownStatusIs400()
    {
        var tenant = await TestApp.CreateTenantAsync("North", "north-ann");
        await MarketDataHttp.AddInstrumentAsync(tenant.Id, "ACME.SIM", "USD");
        await DeliverAsync(Item("w-CA1"), Item("w-CA2", kind: null));
        Feed.Add(Connection, Item("w-CA3"));
        await PullAsync();
        var token = (await AccountHttp.SignInSystemAdmin()).AccessToken;

        Ids(await CorporateActionHttp.GetAsync(token, $"{Path}?status=Skipped")).ShouldBe(["w-CA2-V1"]);
        Ids(await CorporateActionHttp.GetAsync(token, $"{Path}?status=received,Processed")).ShouldBe(["w-CA3-V1", "w-CA1-V1"]);
        Ids(await CorporateActionHttp.GetAsync(token, $"{Path}?connection={Connection}")).Count.ShouldBe(3);
        Ids(await CorporateActionHttp.GetAsync(token, $"{Path}?connection=other")).ShouldBeEmpty();
        (await CorporateActionHttp.ErrorsAsync(await CorporateActionHttp.Send(HttpMethod.Get, $"{Path}?status=Done", token)))
            .ShouldContainKey("Status");
    }

    [Test]
    public async Task TenantAdminAdviserAndCustomer_Return403_AndNoBearer401()
    {
        var world = await NominatedWorld.CreateAsync();

        foreach (var token in new[] { world.TenantAdmin.AccessToken, world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            (await CorporateActionHttp.Send(HttpMethod.Get, Path, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var anonymous = await CorporateActionHttp.Send(HttpMethod.Get, Path, null);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TheConnectionItem_ShowsWhenTheCorporateActionsWereLastRead_AndWhyTheLastReadFailed()
    {
        Feed.Add(Connection, Item());
        await PullAsync();
        Feed.Add(Connection, Item("w-CA2", ratioNew: 0));
        await PullAsync();
        var token = (await AccountHttp.SignInSystemAdmin()).AccessToken;

        var item = (await CorporateActionHttp.GetAsync(token, "/market-data-connections")).GetProperty("items")[0];

        var state = item.GetProperty("corporateActions");
        state.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["lastSucceededAt", "lastFailedAt", "lastFailure"], ignoreOrder: true);
        state.GetProperty("lastSucceededAt").ValueKind.ShouldBe(JsonValueKind.String);
        state.GetProperty("lastFailedAt").ValueKind.ShouldBe(JsonValueKind.String);
        state.GetProperty("lastFailure").GetString().ShouldBe("InvalidItem");
        item.GetProperty("prices").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static List<string> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("outsideId").GetString()!).ToList();
}
