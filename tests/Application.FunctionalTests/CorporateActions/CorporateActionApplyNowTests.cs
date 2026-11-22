using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R20–R22: Apply now makes a hand-entered action effective and stores one
/// <c>Pending</c> entitlement per Custodial holder.</summary>
[Parallelizable]
public class CorporateActionApplyNowTests : TestBase
{
    [Test]
    public async Task ApplyNow_BeforeTheExDate_Returns400AtExDate()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: Today.AddDays(1)));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        var errors = await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion }));

        errors["ExDate"].ShouldBe(["A corporate action cannot be applied before its ex-date."]);
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Announced");
        (await EntitlementsAsync(id)).ShouldBeEmpty();
    }

    [Test]
    public async Task ApplyNow_OnTheExDate_IsEffective_WithOnePendingEntitlementPerCustodialHolder()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(holders.InstrumentId, exDate: Today));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        var response = await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("status").GetString().ShouldBe("Effective");
        item.GetProperty("effectiveAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        item.GetProperty("effectiveBy").GetProperty("name").GetString().ShouldBe("Tess Admin");
        var counts = item.GetProperty("entitlements");
        (counts.GetProperty("total").GetInt32(), counts.GetProperty("pending").GetInt32()).ShouldBe((2, 2));

        var entitlements = await EntitlementsAsync(id);
        entitlements.Select(row => row.AccountId).ShouldBe([await AccountIdAsync(holders.Ned), await AccountIdAsync(holders.Olga)]);
        entitlements.ShouldAllBe(row => row.Status == EntitlementStatus.Pending && row.TransactionId == null);
    }

    [Test]
    public async Task ApplyNow_AfterTheExDate_IsAllowed_AndASecondApplyReturns400AtStatus()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: Today.AddDays(-3)));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        (await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fresh = await RowVersionAsync(world.TenantAdmin.AccessToken, id);
        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion = fresh })))["Status"]
            .ShouldBe(["This corporate action is Effective and cannot be applied."]);
        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken, new { rowVersion = fresh, note = "No" })))["Status"]
            .ShouldBe(["This corporate action is Effective and cannot be cancelled."]);
    }

    [Test]
    public async Task ApplyNow_OfAFeedAction_Returns400AtOrigin_AndOfAnAppliedAction_AtStatus()
    {
        var world = await NominatedWorld.CreateAsync();
        var acme = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "ACME.SIM");
        var feed = await AddFeedActionAsync(acme, exDate: Today.AddDays(-1));
        var aapl = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var applied = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(aapl, exDate: Today));
        await MarkAppliedAsync(applied);

        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{feed}/apply", world.TenantAdmin.AccessToken,
                new { rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, feed) })))["Origin"]
            .ShouldBe(["This corporate action comes from a market data connection and cannot be changed by hand."]);
        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{applied}/apply", world.TenantAdmin.AccessToken,
                new { rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, applied) })))["Status"]
            .ShouldBe(["This corporate action is Applied and cannot be applied."]);
        (await ItemAsync(world.TenantAdmin.AccessToken, feed)).GetProperty("status").GetString().ShouldBe("Announced");
    }

    [Test]
    public async Task ApplyNow_WithAStaleRowVersion_Returns409_AndWithoutOne_400()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId, exDate: Today));

        var stale = await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion = StaleRowVersion });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(stale)).Code.ShouldBe("stale_row_version");
        (await ErrorsAsync(await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { })))
            .ShouldContainKey("RowVersion");
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Announced");
    }

    [Test]
    public async Task ApplyNow_ByAnAdviser_Returns403_AndOnAnotherTenantsAction_404()
    {
        var north = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var southInstrument = await CustodialHttp.CreateInstrumentAsync(south.TenantAdmin.AccessToken, "AAPL");
        var southAction = await CreateAsync(south.TenantAdmin.AccessToken, CreateBody(southInstrument, exDate: Today));
        var rowVersion = await RowVersionAsync(south.TenantAdmin.AccessToken, southAction);

        (await Send(HttpMethod.Post, $"{Root}/{southAction}/apply", south.AdviserToken.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, $"{Root}/{southAction}/apply", north.TenantAdmin.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, $"{Root}/{southAction}/apply", (await AccountHttp.SignInSystemAdmin()).AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ItemAsync(south.TenantAdmin.AccessToken, southAction)).GetProperty("status").GetString().ShouldBe("Announced");
    }
}
