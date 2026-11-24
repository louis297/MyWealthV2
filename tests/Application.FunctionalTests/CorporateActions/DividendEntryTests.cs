using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R1, R3, R7, R11, R49: entering a dividend by hand and reading it.</summary>
[Parallelizable]
public class DividendEntryTests : TestBase
{
    [Test]
    public async Task TenantAdmin_EntersADividend_AndTheItemShowsItsTermsAndNoRatio()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var exDate = Today.AddDays(9);

        var id = await CreateAsync(world.TenantAdmin.AccessToken, DividendBody(instrumentId, exDate: exDate));

        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("kind").GetString().ShouldBe("Dividend");
        item.GetProperty("ratioNew").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("ratioHeld").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("origin").GetString().ShouldBe("Manual");
        item.GetProperty("status").GetString().ShouldBe("Announced");
        item.GetProperty("exDate").GetString().ShouldBe(exDate.ToString("yyyy-MM-dd"));
        item.GetProperty("paymentDate").GetString().ShouldBe(exDate.AddDays(7).ToString("yyyy-MM-dd"));
        item.GetProperty("memo").GetString().ShouldBe("Quarterly dividend");
        var dividend = item.GetProperty("dividend");
        dividend.GetProperty("amountPerShare").GetDecimal().ShouldBe(0.26m);
        dividend.GetProperty("currency").GetString().ShouldBe("USD");
        dividend.GetProperty("taxSource").GetString().ShouldBe("Foreign");
        dividend.GetProperty("imputationCreditPerShare").GetDecimal().ShouldBe(0m);
        dividend.GetProperty("foreignTaxRatePercent").GetDecimal().ShouldBe(15m);
        dividend.GetProperty("reinvestmentPrice").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ASplit_HasNoDividend()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));

        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("dividend").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TheCreditAndForeignRate_DefaultToZero()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var body = DividendBody(instrumentId, taxSource: "NewZealand", rate: null);
        body.Remove("imputationCreditPerShare");
        body.Remove("foreignTaxRatePercent");

        var id = await CreateAsync(world.TenantAdmin.AccessToken, body);

        var dividend = (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("dividend");
        dividend.GetProperty("imputationCreditPerShare").GetDecimal().ShouldBe(0m);
        dividend.GetProperty("foreignTaxRatePercent").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task TenantAdmin_ChangesTheTerms_WithoutAKind_AndCancelsIt()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, DividendBody(instrumentId));
        var body = DividendBody(instrumentId, amount: 0.72m, taxSource: "NewZealand", credit: 0.28m, rate: 0m,
            exDate: Today.AddDays(12));
        body.Remove("instrumentId");
        body.Remove("kind");
        body["rowVersion"] = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        (await Send(HttpMethod.Put, $"{Root}/{id}", world.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("exDate").GetString().ShouldBe(Today.AddDays(12).ToString("yyyy-MM-dd"));
        var dividend = item.GetProperty("dividend");
        dividend.GetProperty("amountPerShare").GetDecimal().ShouldBe(0.72m);
        dividend.GetProperty("taxSource").GetString().ShouldBe("NewZealand");
        dividend.GetProperty("imputationCreditPerShare").GetDecimal().ShouldBe(0.28m);

        (await Send(HttpMethod.Post, $"{Root}/{id}/cancel", world.TenantAdmin.AccessToken,
                new { rowVersion = item.GetProperty("rowVersion").GetString(), note = "Withdrawn by the issuer" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Cancelled");
    }

    [TestCase("amountPerShare", 0, "AmountPerShare")]
    [TestCase("amountPerShare", 1000000.01, "AmountPerShare")]
    [TestCase("amountPerShare", 0.123456789, "AmountPerShare")]
    [TestCase("taxSource", "Mars", "TaxSource")]
    [TestCase("taxSource", null, "TaxSource")]
    [TestCase("imputationCreditPerShare", 0.1, "ImputationCreditPerShare")]
    [TestCase("foreignTaxRatePercent", 100, "ForeignTaxRatePercent")]
    [TestCase("foreignTaxRatePercent", -1, "ForeignTaxRatePercent")]
    [TestCase("foreignTaxRatePercent", 15.001, "ForeignTaxRatePercent")]
    public async Task TermsOutsideTheRule_Return400AtTheField(string field, object? value, string error)
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var body = DividendBody(instrumentId);
        body[field] = value;

        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, body))).ShouldContainKey(error);
    }

    [Test]
    public async Task ACreditAboveTheAmount_OrAForeignRateOnANewZealandCompany_Returns400AtTheField()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");

        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken,
                DividendBody(instrumentId, amount: 0.72m, taxSource: "NewZealand", credit: 0.73m, rate: 0m))))
            .ShouldContainKey("ImputationCreditPerShare");
        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken,
                DividendBody(instrumentId, taxSource: "NewZealand", rate: 15m))))
            .ShouldContainKey("ForeignTaxRatePercent");
    }

    [Test]
    public async Task AReinvestmentPrice_ByHand_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var body = DividendBody(instrumentId);
        body["reinvestmentPrice"] = 25m;

        var errors = await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, body));

        errors["ReinvestmentPrice"].ShouldContain("Reinvestment is available only for dividends that come from a market data connection.");
    }

    [Test]
    public async Task ARatioOnADividend_Returns400AtRatioNew_AndTermsOnASplit_Return400AtTheTerm()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var dividend = DividendBody(instrumentId);
        dividend["ratioNew"] = 2;
        dividend["ratioHeld"] = 1;
        var split = CreateBody(instrumentId);
        split["amountPerShare"] = 0.72m;

        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, dividend))).ShouldContainKey("RatioNew");
        (await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, split))).ShouldContainKey("AmountPerShare");
    }

    [Test]
    public async Task TheKindOfADividend_DoesNotChange()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, DividendBody(instrumentId));
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        (await ErrorsAsync(await Send(HttpMethod.Put, $"{Root}/{id}", world.TenantAdmin.AccessToken, UpdateBody(rowVersion))))
            .ShouldContainKey("Kind");
    }

    [Test]
    public async Task ASecondDividend_WithTheSameExDate_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        await CreateAsync(world.TenantAdmin.AccessToken, DividendBody(instrumentId));

        var errors = await ErrorsAsync(await Send(HttpMethod.Post, Root, world.TenantAdmin.AccessToken, DividendBody(instrumentId)));

        errors["ExDate"].ShouldContain("A dividend of AAPL with this ex-date already exists.");
    }

    [Test]
    public async Task Adviser_ReadsADividend_ButCannotEnterOne_AndACustomerGets403()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var id = await CreateAsync(world.TenantAdmin.AccessToken, DividendBody(instrumentId));

        (await ItemAsync(world.AdviserToken.AccessToken, id)).GetProperty("dividend").GetProperty("amountPerShare")
            .GetDecimal().ShouldBe(0.26m);
        (await Send(HttpMethod.Post, Root, world.AdviserToken.AccessToken, DividendBody(instrumentId, exDate: Today.AddDays(20))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, Root, world.CustomerToken.AccessToken, DividendBody(instrumentId, exDate: Today.AddDays(20))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TheKindFilter_TakesOneOrMoreNames_AndAnUnknownOneReturns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AAPL");
        var dividendId = await CreateAsync(world.TenantAdmin.AccessToken, DividendBody(instrumentId));
        var splitId = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(instrumentId));
        var token = world.TenantAdmin.AccessToken;

        static IEnumerable<Guid> Ids(JsonElement page) =>
            page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid());

        Ids(await GetAsync(token, $"{Root}?kind=Dividend")).ShouldBe([dividendId]);
        Ids(await GetAsync(token, $"{Root}?kind=split")).ShouldBe([splitId]);
        Ids(await GetAsync(token, $"{Root}?kind=Split,Dividend")).ShouldBe([dividendId, splitId], ignoreOrder: true);
        (await ErrorsAsync(await Send(HttpMethod.Get, $"{Root}?kind=Rights", token))).ShouldContainKey("Kind");
    }

    [Test]
    public async Task TheDividendOfAnotherTenant_Returns404_OnEveryRoute()
    {
        var world = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var southInstrument = await CustodialHttp.CreateInstrumentAsync(south.TenantAdmin.AccessToken, "AAPL");
        var southId = await CreateAsync(south.TenantAdmin.AccessToken, DividendBody(southInstrument));
        var rowVersion = await RowVersionAsync(south.TenantAdmin.AccessToken, southId);
        var token = world.TenantAdmin.AccessToken;
        var update = DividendBody(southInstrument);
        update.Remove("instrumentId");
        update["rowVersion"] = rowVersion;

        (await Send(HttpMethod.Get, $"{Root}/{southId}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"{Root}/{southId}/entitlements", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Put, $"{Root}/{southId}", token, update)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, $"{Root}/{southId}/cancel", token, new { rowVersion, note = "No" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, $"{Root}/{southId}/apply", token, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, Root, token, DividendBody(southInstrument))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
