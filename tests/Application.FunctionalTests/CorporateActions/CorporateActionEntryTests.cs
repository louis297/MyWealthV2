using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R1, R3, R15–R19, R21, R34, R35: entering a corporate action by hand and reading it.</summary>
[Parallelizable]
public class CorporateActionEntryTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_WithoutRedirect()
    {
        foreach (var (method, path, body) in Routes(Guid.NewGuid(), Guid.NewGuid()))
        {
            var response = await Send(method, path, null, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task CustomerAndSystemAdmin_Return403_OnEveryRoute()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { world.CustomerToken.AccessToken, system.AccessToken })
        {
            foreach (var (method, path, body) in Routes(id, instrumentId))
            {
                (await Send(method, path, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
            }
        }
    }

    [Test]
    public async Task Adviser_Reads_ButEveryWriteReturns403()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);
        var token = world.AdviserToken.AccessToken;

        (await GetAsync(token, Root)).GetProperty("totalCount").GetInt32().ShouldBe(1);
        (await ItemAsync(token, id)).GetProperty("id").GetGuid().ShouldBe(id);
        (await Send(HttpMethod.Post, Root, token, CreateBody(instrumentId, exDate: Today.AddDays(20))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Put, $"{Root}/{id}", token, UpdateBody(rowVersion))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, $"{Root}/{id}/cancel", token, new { rowVersion, note = "No" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_Creates_AndTheItemShowsTheEvent()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var exDate = Today.AddDays(9);

        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: exDate, memo: "  Announced on 20 Nov  "));

        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(instrumentId);
        item.GetProperty("instrument").GetProperty("symbol").GetString().ShouldBe("AAPL");
        item.GetProperty("instrument").GetProperty("name").GetString().ShouldBe("AAPL fund");
        item.GetProperty("kind").GetString().ShouldBe("Split");
        item.GetProperty("ratioNew").GetInt32().ShouldBe(4);
        item.GetProperty("ratioHeld").GetInt32().ShouldBe(1);
        item.GetProperty("exDate").GetString().ShouldBe(exDate.ToString("yyyy-MM-dd"));
        item.GetProperty("recordDate").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("paymentDate").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("origin").GetString().ShouldBe("Manual");
        item.GetProperty("status").GetString().ShouldBe("Announced");
        item.GetProperty("memo").GetString().ShouldBe("Announced on 20 Nov");
        item.GetProperty("createdBy").GetProperty("name").GetString().ShouldBe("Tess Admin");
        item.GetProperty("createdAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        foreach (var field in new[] { "effectiveAt", "effectiveBy", "appliedAt", "cancelledAt", "cancelledBy", "cancelNote" })
        {
            item.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }

        var counts = item.GetProperty("entitlements");
        (counts.GetProperty("total").GetInt32(), counts.GetProperty("posted").GetInt32(),
            counts.GetProperty("pending").GetInt32(), counts.GetProperty("notApplied").GetInt32()).ShouldBe((0, 0, 0, 0));
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task TenantAdmin_Updates_AndTheInstrumentStays()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        var response = await Send(HttpMethod.Put, $"{Root}/{id}", world.TenantAdmin.AccessToken, UpdateBody(
            rowVersion, "Bonus", 1, 5, Today.AddDays(12), Today.AddDays(13), Today.AddDays(20), "Corrected"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("kind").GetString().ShouldBe("Bonus");
        (item.GetProperty("ratioNew").GetInt32(), item.GetProperty("ratioHeld").GetInt32()).ShouldBe((1, 5));
        item.GetProperty("exDate").GetString().ShouldBe(Today.AddDays(12).ToString("yyyy-MM-dd"));
        item.GetProperty("recordDate").GetString().ShouldBe(Today.AddDays(13).ToString("yyyy-MM-dd"));
        item.GetProperty("paymentDate").GetString().ShouldBe(Today.AddDays(20).ToString("yyyy-MM-dd"));
        item.GetProperty("memo").GetString().ShouldBe("Corrected");
        item.GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(instrumentId);
        item.GetProperty("rowVersion").GetString().ShouldNotBe(rowVersion);
    }

    [Test]
    public async Task TenantAdmin_Cancels_WithANote()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        var response = await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken,
            new { rowVersion, note = " Entered for the wrong instrument " });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("cancelNote").GetString().ShouldBe("Entered for the wrong instrument");
        item.GetProperty("cancelledBy").GetProperty("name").GetString().ShouldBe("Tess Admin");
        item.GetProperty("cancelledAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Test]
    public async Task Cancel_WithoutANote_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken, new { rowVersion, note = "  " })))
            .ShouldContainKey("Note");
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Announced");
    }

    [TestCase("Split", 1, 2)]
    [TestCase("Split", 1, 1)]
    [TestCase("Consolidation", 2, 1)]
    [TestCase("Split", 10001, 1)]
    [TestCase("Bonus", 0, 1)]
    [TestCase("Bonus", 1, 0)]
    public async Task ARatioThatBreaksR3_Returns400AtRatioNew(string kind, int ratioNew, int ratioHeld)
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");

        var errors = await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken,
            CreateBody(instrumentId, kind, ratioNew, ratioHeld)));

        errors.ShouldContainKey("RatioNew");
        (await GetAsync(world.TenantAdmin.AccessToken, Root)).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task AnUnknownKind_Returns400AtKind()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");

        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, CreateBody(instrumentId, "Dividend"))))
            .ShouldContainKey("Kind");
    }

    [Test]
    public async Task AnExDateTooFarAhead_OrMissing_Returns400AtExDate()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");

        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: Today.AddDays(367)))))
            .ShouldContainKey("ExDate");
        var missing = CreateBody(instrumentId);
        missing.Remove("exDate");
        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, missing))).ShouldContainKey("ExDate");
        await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: Today.AddDays(366)));
    }

    [Test]
    public async Task ADuplicateOfInstrumentKindAndExDate_Returns400_AndIsAllowedAgainAfterACancel()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));

        var errors = await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, CreateBody(instrumentId, ratioNew: 2)));
        errors["ExDate"].ShouldBe(["A split of AAPL with this ex-date already exists."]);
        await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, "Bonus", 1, 5));

        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);
        (await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken, new { rowVersion, note = "Wrong" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, ratioNew: 2));
    }

    [Test]
    public async Task AnUpdateOntoAnotherActionsExDate_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: Today.AddDays(9)));
        var second = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: Today.AddDays(10)));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, second);

        var errors = await ErrorsAsync(await Send(HttpMethod.Put, $"{Root}/{second}", world.TenantAdmin.AccessToken,
            UpdateBody(rowVersion, exDate: Today.AddDays(9))));

        errors["ExDate"].ShouldBe(["A split of AAPL with this ex-date already exists."]);
        (await Send(HttpMethod.Put, $"{Root}/{second}", world.TenantAdmin.AccessToken, UpdateBody(rowVersion, exDate: Today.AddDays(10))))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task AServedInstrument_Returns400AtInstrumentId()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "ACME.SIM");

        var errors = await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, CreateBody(instrumentId)));

        errors["InstrumentId"].ShouldBe(["Corporate actions of ACME.SIM come from its market data connection."]);
    }

    [Test]
    public async Task ADisabledInstrument_IsAccepted()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Instruments] SET [IsActive] = 0 WHERE [PublicId] = {instrumentId}");
        }

        await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
    }

    [Test]
    public async Task AMemoThatIsBlankOrTooLong_Returns400AtMemo()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");

        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, CreateBody(instrumentId, memo: "  "))))
            .ShouldContainKey("Memo");
        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, CreateBody(instrumentId, memo: new string('m', 201)))))
            .ShouldContainKey("Memo");
        await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, memo: null));
    }

    [Test]
    public async Task UpdateAndCancelOfAFeedAction_Return400AtOrigin()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "ACME.SIM");
        var id = await AddFeedActionAsync(instrumentId, exDate: Today.AddDays(-1));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);
        const string message = "This corporate action comes from a market data connection and cannot be changed by hand.";

        (await ErrorsAsync(await Send(HttpMethod.Put, $"{Root}/{id}", world.TenantAdmin.AccessToken, UpdateBody(rowVersion))))["Origin"]
            .ShouldBe([message]);
        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken, new { rowVersion, note = "No" })))["Origin"]
            .ShouldBe([message]);
        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("status").GetString().ShouldBe("Announced");
        item.GetProperty("origin").GetString().ShouldBe("Feed");
        item.GetProperty("createdBy").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task UpdateAndCancelOfAnAppliedAction_Return400AtStatus()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
        await MarkAppliedAsync(id);
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        (await ErrorsAsync(await Send(HttpMethod.Put, $"{Root}/{id}", world.TenantAdmin.AccessToken, UpdateBody(rowVersion))))["Status"]
            .ShouldBe(["This corporate action is Applied and cannot be changed."]);
        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken, new { rowVersion, note = "No" })))["Status"]
            .ShouldBe(["This corporate action is Applied and cannot be cancelled."]);
    }

    [Test]
    public async Task AStaleRowVersion_Returns409()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));

        foreach (var response in new[]
                 {
                     await Send(HttpMethod.Put, $"{Root}/{id}", world.TenantAdmin.AccessToken, UpdateBody(StaleRowVersion)),
                     await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken, new { rowVersion = StaleRowVersion, note = "No" })
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("stale_row_version");
        }

        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Announced");
    }

    [Test]
    public async Task ATenantIdInAQueryOrBody_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var body = CreateBody(instrumentId);
        body["tenantId"] = world.Tenant.PublicId;

        (await Send(HttpMethod.Get, $"{Root}?tenantId={world.Tenant.PublicId}", world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TheList_IsLatestExDateFirst_AndFilters()
    {
        var world = await NominatedWorld.CreateAsync();
        var aapl = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var msft = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "MSFT");
        var acme = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "ACME.SIM");
        var early = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(aapl, exDate: Today.AddDays(5)));
        var late = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(msft, exDate: Today.AddDays(40)));
        var cancelled = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(aapl, "Bonus", 1, 5, Today.AddDays(20)));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, cancelled);
        (await Send(HttpMethod.Post, $"{Root}/{cancelled}/cancel", world.TenantAdmin.AccessToken, new { rowVersion, note = "No" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var feed = await AddFeedActionAsync(acme, exDate: Today.AddDays(30));
        var token = world.TenantAdmin.AccessToken;

        Ids(await GetAsync(token, Root)).ShouldBe([late, feed, cancelled, early]);
        Ids(await GetAsync(token, $"{Root}?status=Announced")).ShouldBe([late, feed, early]);
        Ids(await GetAsync(token, $"{Root}?status=cancelled,Announced")).ShouldBe([late, feed, cancelled, early]);
        Ids(await GetAsync(token, $"{Root}?instrumentId={aapl}")).ShouldBe([cancelled, early]);
        Ids(await GetAsync(token, $"{Root}?instrumentId={Guid.NewGuid()}")).ShouldBeEmpty();
        Ids(await GetAsync(token, $"{Root}?origin=Feed")).ShouldBe([feed]);
        Ids(await GetAsync(token, $"{Root}?origin=Manual&pageSize=2")).ShouldBe([late, cancelled]);
        (await ErrorsAsync(await Send(HttpMethod.Get, $"{Root}?status=Pending", token))).ShouldContainKey("Status");
        (await ErrorsAsync(await Send(HttpMethod.Get, $"{Root}?origin=Bank", token))).ShouldContainKey("Origin");
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsActionOrInstrument_Returns404()
    {
        var north = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var southInstrument = await CustodialHttp.CreateInstrumentAsync(south.TenantAdmin.AccessToken, "AAPL");
        var southAction = await CreateAsync(south.TenantAdmin.AccessToken, CreateBody(southInstrument));
        var rowVersion = await RowVersionAsync(south.TenantAdmin.AccessToken, southAction);
        var token = north.TenantAdmin.AccessToken;

        (await Send(HttpMethod.Get, $"{Root}/{southAction}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Put, $"{Root}/{southAction}", token, UpdateBody(rowVersion))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, $"{Root}/{southAction}/cancel", token, new { rowVersion, note = "No" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, Root, token, CreateBody(southInstrument))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(token, Root)).GetProperty("totalCount").GetInt32().ShouldBe(0);
        Ids(await GetAsync(token, $"{Root}?instrumentId={southInstrument}")).ShouldBeEmpty();
        (await ItemAsync(south.TenantAdmin.AccessToken, southAction)).GetProperty("status").GetString().ShouldBe("Announced");
    }

    private static List<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
}
