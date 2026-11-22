using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R35–R37: the entitlements of a corporate action and the corporate action on the
/// Transaction item.</summary>
[Parallelizable]
public class CorporateActionReadTests : TestBase
{
    [Test]
    public async Task Entitlements_ForATenantAdmin_ListEveryAccount_WithBothQuantitiesAndTheTransaction()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        (await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken, CustodialHttp.Trade(holders.Olga, "Sell", holders.InstrumentId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await RunApplyAsync();

        var page = await GetAsync(world.TenantAdmin.AccessToken, $"{Root}/{id}/entitlements");

        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        var ned = items[0];
        ned.GetProperty("accountId").GetGuid().ShouldBe(holders.Ned);
        ned.GetProperty("accountName").GetString().ShouldBe("Managed portfolio");
        ned.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        ned.GetProperty("customerName").GetString().ShouldBe("Ned North");
        ned.GetProperty("status").GetString().ShouldBe("Posted");
        ned.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        (ned.GetProperty("quantityBefore").GetDecimal(), ned.GetProperty("quantityAfter").GetDecimal()).ShouldBe((100m, 200m));
        ned.GetProperty("transactionId").GetGuid().ShouldNotBe(Guid.Empty);
        ned.GetProperty("postedAt").ValueKind.ShouldBe(JsonValueKind.String);
        var olga = items[1];
        olga.GetProperty("accountId").GetGuid().ShouldBe(holders.Olga);
        olga.GetProperty("customerName").GetString().ShouldBe("Olga Other");
        olga.GetProperty("status").GetString().ShouldBe("NotApplied");
        olga.GetProperty("reason").GetString().ShouldBe("NoHolding");
        foreach (var field in new[] { "quantityBefore", "quantityAfter", "transactionId" })
        {
            olga.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }

        var notApplied = await GetAsync(world.TenantAdmin.AccessToken, $"{Root}/{id}/entitlements?status=NotApplied");
        notApplied.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("accountId").GetGuid()).ShouldBe([holders.Olga]);
        (await ErrorsAsync(await Send(HttpMethod.Get, $"{Root}/{id}/entitlements?status=Done", world.TenantAdmin.AccessToken)))
            .ShouldContainKey("Status");
        var counts = (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("entitlements");
        (counts.GetProperty("total").GetInt32(), counts.GetProperty("posted").GetInt32(), counts.GetProperty("notApplied").GetInt32())
            .ShouldBe((2, 1, 1));
    }

    [Test]
    public async Task Entitlements_ForAnAdviser_ListOnlyTheAssignedCustomersAccounts_AndSoDoTheCounts()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));

        var page = await GetAsync(world.AdviserToken.AccessToken, $"{Root}/{id}/entitlements");

        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("accountId").GetGuid()).ShouldBe([holders.Ned]);
        var counts = (await ItemAsync(world.AdviserToken.AccessToken, id)).GetProperty("entitlements");
        (counts.GetProperty("total").GetInt32(), counts.GetProperty("pending").GetInt32()).ShouldBe((1, 1));
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("entitlements").GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Test]
    public async Task Entitlements_OfAnotherTenantsAction_Return404()
    {
        var north = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var southInstrument = await CustodialHttp.CreateInstrumentAsync(south.TenantAdmin.AccessToken, "AAPL");
        var southAction = await CreateAsync(south.TenantAdmin.AccessToken, CreateBody(southInstrument));

        (await Send(HttpMethod.Get, $"{Root}/{southAction}/entitlements", north.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(south.TenantAdmin.AccessToken, $"{Root}/{southAction}/entitlements")).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task TheTransactionItem_CarriesTheCorporateAction_ForEveryCallerWhoCanReadIt_TheCustomerIncluded()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        await RunApplyAsync();
        var postingId = (await GetAsync(world.TenantAdmin.AccessToken, $"{Root}/{id}/entitlements"))
            .GetProperty("items")[0].GetProperty("transactionId").GetGuid();

        foreach (var token in new[] { world.TenantAdmin.AccessToken, world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            var item = await GetAsync(token, $"/transactions/{postingId}");
            item.GetProperty("source").GetProperty("kind").GetString().ShouldBe("CorporateAction");
            item.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(id);
            var detail = item.GetProperty("corporateAction");
            detail.GetProperty("id").GetGuid().ShouldBe(id);
            detail.GetProperty("kind").GetString().ShouldBe("Split");
            (detail.GetProperty("ratioNew").GetInt32(), detail.GetProperty("ratioHeld").GetInt32()).ShouldBe((2, 1));
            detail.GetProperty("exDate").GetString().ShouldBe(Today.ToString("yyyy-MM-dd"));
            detail.GetProperty("recordDate").ValueKind.ShouldBe(JsonValueKind.Null);
            detail.GetProperty("paymentDate").ValueKind.ShouldBe(JsonValueKind.Null);
            (detail.GetProperty("quantityBefore").GetDecimal(), detail.GetProperty("quantityAfter").GetDecimal()).ShouldBe((100m, 200m));
        }

        var list = await GetAsync(world.CustomerToken.AccessToken, $"/transactions?accountId={holders.Ned}");
        var rows = list.GetProperty("items").EnumerateArray().ToList();
        rows.Single(row => row.GetProperty("id").GetGuid() == postingId).GetProperty("corporateAction").GetProperty("id").GetGuid().ShouldBe(id);
        rows.Where(row => row.GetProperty("id").GetGuid() != postingId)
            .ShouldAllBe(row => row.GetProperty("corporateAction").ValueKind == JsonValueKind.Null);
    }

    [Test]
    public async Task TheCustomer_CannotReadCorporateActions()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));

        (await Send(HttpMethod.Get, $"{Root}/{id}/entitlements", world.CustomerToken.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Get, $"{Root}/{id}/entitlements", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> ApplyNowAsync(NominatedWorld world, Dictionary<string, object?> body)
    {
        var id = await CreateAsync(world.TenantAdmin.AccessToken, body);
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);
        (await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return id;
    }
}
