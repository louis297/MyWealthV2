using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

/// <summary>Instrument-tax-attributes R1, R5, R9, R12, R16–R19, R21: <c>PUT /instruments/{id}/tax-attributes</c>.</summary>
[Parallelizable]
public class SetInstrumentTaxAttributesTests : TestBase
{
    private const string TreatmentMessage =
        "Treatment must be NotForeign, ExemptAsxListed, ExemptOther, Attributing, or AttributingNoFdr.";

    private static int Latest => DateTime.UtcNow.Year + 1;

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await Put(Guid.NewGuid(), accessToken: null, new { issuerCountry = "US", fifTreatments = Array.Empty<object>(), rowVersion = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Adviser_Returns403_AndNothingChanges()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await Put(id, tokens.AccessToken, Body("US", [Row(2027, "Attributing")], await RowVersion(id, tokens.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        var response = await Put(Guid.NewGuid(), tokens.AccessToken, Body("US", [], "x"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_Returns204_AndTheItemShowsTheCountryAndTheRowsSortedByTaxYear()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "BHP", "AUD");

        var response = await Put(id, tokens.AccessToken, Body("AU",
        [
            Row(2027, "ExemptAsxListed", "Checked on 2026-11-27."),
            Row(2026, "ExemptAsxListed")
        ], await RowVersion(id, tokens.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").GetString().ShouldBe("AU");
        Rows(item.RootElement).ShouldBe(
        [
            (2026, "ExemptAsxListed", (string?)null),
            (2027, "ExemptAsxListed", "Checked on 2026-11-27.")
        ]);
    }

    [Test]
    public async Task SystemAdmin_Returns204()
    {
        var (_, tenantAdmin) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tenantAdmin.AccessToken);
        var systemAdmin = await InstrumentHttp.SignInSystemAdmin();

        var response = await Put(id, systemAdmin.AccessToken, Body("US", [Row(2027, "Attributing")], await RowVersion(id, tenantAdmin.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(id, tenantAdmin.AccessToken);
        item.RootElement.GetProperty("issuerCountry").GetString().ShouldBe("US");
        Rows(item.RootElement).ShouldBe([(2027, "Attributing", (string?)null)]);
    }

    [Test]
    public async Task ACountryIsTrimmedAndStoredUpperCase_AndAnEmptyNoteIsNone()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await Put(id, tokens.AccessToken, Body(" us ", [Row(2027, "Attributing", "  ")], await RowVersion(id, tokens.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").GetString().ShouldBe("US");
        Rows(item.RootElement).ShouldBe([(2027, "Attributing", (string?)null)]);
    }

    [Test]
    public async Task ASecondWrite_UpdatesAddsAndRemovesRowsByTaxYear()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "BHP", "AUD");
        (await Put(id, tokens.AccessToken, Body("AU",
        [
            Row(2026, "ExemptAsxListed"),
            Row(2027, "ExemptAsxListed")
        ], await RowVersion(id, tokens.AccessToken)))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await Put(id, tokens.AccessToken, Body("AU",
        [
            Row(2027, "Attributing", "Left the list."),
            Row(Latest, "Attributing")
        ], await RowVersion(id, tokens.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(id, tokens.AccessToken);
        Rows(item.RootElement).ShouldBe(
        [
            (2027, "Attributing", (string?)"Left the list."),
            (Latest, "Attributing", null)
        ]);
    }

    [Test]
    public async Task TheEmptyBody_ClearsEverything()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        (await Put(id, tokens.AccessToken, Body("US", [Row(2027, "Attributing")], await RowVersion(id, tokens.AccessToken))))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await Put(id, tokens.AccessToken, Body(null, [], await RowVersion(id, tokens.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").ValueKind.ShouldBe(JsonValueKind.Null);
        Rows(item.RootElement).ShouldBeEmpty();
    }

    [Test]
    public async Task ADisabledInstrument_CanBeClassified()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        (await InstrumentHttp.Send(HttpMethod.Post, $"/instruments/{id}/disable", tokens.AccessToken,
            new { rowVersion = await RowVersion(id, tokens.AccessToken) })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await Put(id, tokens.AccessToken, Body("US", [Row(2027, "Attributing")], await RowVersion(id, tokens.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        item.RootElement.GetProperty("issuerCountry").GetString().ShouldBe("US");
    }

    [Test]
    public async Task TheRowVersionMoves_WhenOnlyARowChanges_AndWhenTheBodyEqualsWhatIsStored()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        (await Put(id, tokens.AccessToken, Body("US", [Row(2027, "Attributing")], await RowVersion(id, tokens.AccessToken))))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var first = await RowVersion(id, tokens.AccessToken);

        (await Put(id, tokens.AccessToken, Body("US", [Row(2027, "AttributingNoFdr")], first))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var second = await RowVersion(id, tokens.AccessToken);
        second.ShouldNotBe(first);

        (await Put(id, tokens.AccessToken, Body("US", [Row(2027, "AttributingNoFdr")], second))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var third = await RowVersion(id, tokens.AccessToken);
        third.ShouldNotBe(second);

        (await Put(id, tokens.AccessToken, Body("US", [Row(2027, "Attributing")], first))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await Put(id, tokens.AccessToken, new { issuerCountry = "US", fifTreatments = Array.Empty<object>() });

        (await Errors(response)).ShouldContainKey("RowVersion");
    }

    [Test]
    public async Task StaleRowVersion_Returns409_AndNothingChanges()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await Put(id, tokens.AccessToken, Body("US", [Row(2027, "Attributing")], Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").ValueKind.ShouldBe(JsonValueKind.Null);
        Rows(item.RootElement).ShouldBeEmpty();
    }

    [Test]
    public async Task UnknownId_Returns404()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();

        var response = await Put(Guid.NewGuid(), tokens.AccessToken, Body("US", [], Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MissingIssuerCountry_Returns400AtTheField()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await Put(id, tokens.AccessToken, new { fifTreatments = Array.Empty<object>(), rowVersion = await RowVersion(id, tokens.AccessToken) });

        (await Errors(response)).ShouldContainKey("IssuerCountry");
    }

    [Test]
    public async Task MissingFifTreatments_Returns400AtTheField()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await Put(id, tokens.AccessToken, new { issuerCountry = "US", rowVersion = await RowVersion(id, tokens.AccessToken) });

        (await Errors(response)).ShouldContainKey("FifTreatments");
    }

    [Test]
    public async Task AnyOtherField_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await RowVersion(id, tokens.AccessToken);

        (await Put(id, tokens.AccessToken, new { issuerCountry = "US", fifTreatments = Array.Empty<object>(), name = "Renamed", rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(id, tokens.AccessToken, new
        {
            issuerCountry = "US",
            fifTreatments = new[] { new { taxYear = 2027, treatment = "Attributing", note = (string?)null, extra = 1 } },
            rowVersion
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [TestCase("UK")]
    [TestCase("XX")]
    [TestCase("NZL")]
    [TestCase("")]
    public async Task AnIssuerCountryNotOnTheList_Returns400(string country)
    {
        var errors = await PutErrors(Body(country, [], null));

        errors["IssuerCountry"].ShouldBe(["IssuerCountry must be an ISO 3166-1 alpha-2 country code."]);
    }

    [Test]
    public async Task ATaxYearBefore2008_Returns400()
    {
        var errors = await PutErrors(Body("US", [Row(2007, "Attributing")], null));

        errors["FifTreatments[0].TaxYear"].ShouldBe([$"TaxYear must be between 2008 and {Latest}."]);
    }

    [Test]
    public async Task ATaxYearAboveTheServersYearPlusOne_Returns400_AndTheServersYearPlusOneIsAccepted()
    {
        var errors = await PutErrors(Body("US", [Row(2027, "Attributing"), Row(Latest + 1, "Attributing")], null));
        errors["FifTreatments[1].TaxYear"].ShouldBe([$"TaxYear must be between 2008 and {Latest}."]);

        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin("South Advisory", "south-advisory", "sam@south.example");
        var id = await CreateAsync(tokens.AccessToken);
        (await Put(id, tokens.AccessToken, Body("US", [Row(Latest, "Attributing")], await RowVersion(id, tokens.AccessToken))))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task AMissingTaxYear_Returns400()
    {
        var errors = await PutErrors(Body("US", [new { treatment = "Attributing" }], null));

        errors.ShouldContainKey("FifTreatments[0].TaxYear");
    }

    [TestCase("Unknown")]
    [TestCase("NotClassified")]
    [TestCase("attributing")]
    [TestCase("4")]
    [TestCase(null)]
    public async Task ATreatmentThatIsNotAStoredValue_Returns400(string? treatment)
    {
        var errors = await PutErrors(Body("US", [Row(2027, treatment!)], null));

        errors["FifTreatments[0].Treatment"].ShouldBe([TreatmentMessage]);
    }

    [Test]
    public async Task ANoteAbove500Characters_Returns400()
    {
        var errors = await PutErrors(Body("US", [Row(2027, "Attributing", new string('n', 501))], null));

        errors["FifTreatments[0].Note"].ShouldBe(["Note must be at most 500 characters."]);
    }

    [Test]
    public async Task ExemptOtherWithoutANote_Returns400()
    {
        var errors = await PutErrors(Body("AU", [Row(2027, "ExemptOther")], null));

        errors["FifTreatments[0].Note"].ShouldBe(["Note is required for ExemptOther: name the exemption."]);
    }

    [Test]
    public async Task ATaxYearTwice_Returns400()
    {
        var errors = await PutErrors(Body("US", [Row(2027, "Attributing"), Row(2027, "AttributingNoFdr")], null));

        errors["FifTreatments"].ShouldBe(["Each tax year can be entered once."]);
    }

    [Test]
    public async Task MoreThan50Rows_Returns400()
    {
        var rows = Enumerable.Range(0, 51).Select(offset => Row(Latest - offset, "Attributing")).ToArray();

        var errors = await PutErrors(Body("US", rows, null));

        errors["FifTreatments"].ShouldBe(["At most 50 FIF treatments can be entered."]);
    }

    [Test]
    public async Task ARowWithoutACountry_Returns400()
    {
        var errors = await PutErrors(Body(null, [Row(2027, "Attributing")], null));

        errors["IssuerCountry"].ShouldBe(["IssuerCountry is required when a FIF treatment is set."]);
    }

    [TestCase("NZ", "Attributing", "An issuer resident in New Zealand is NotForeign.")]
    [TestCase("US", "NotForeign", "NotForeign is for an issuer resident in New Zealand.")]
    [TestCase("GB", "ExemptAsxListed", "ExemptAsxListed is for an issuer resident in Australia.")]
    public async Task ATreatmentThatDisagreesWithTheCountry_Returns400(string country, string treatment, string message)
    {
        var errors = await PutErrors(Body(country, [Row(2027, treatment)], null));

        errors["FifTreatments[0].Treatment"].ShouldBe([message]);
    }

    [Test]
    public async Task ACountryWithNoRow_IsAccepted()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await Put(id, tokens.AccessToken, Body("NZ", [], await RowVersion(id, tokens.AccessToken)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").GetString().ShouldBe("NZ");
        Rows(item.RootElement).ShouldBeEmpty();
    }

    [Test]
    public async Task AnAdviser_ReadsBothFieldsOnTheListAndTheItem()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        (await Put(id, tokens.AccessToken, Body("US", [Row(2027, "Attributing", "Ordinary shares.")], await RowVersion(id, tokens.AccessToken))))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann Adviser", "ann@north.example", "Password1!", tenant.Id);
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", "north-advisory");

        using var item = await ReadItem(id, adviser.AccessToken);
        item.RootElement.GetProperty("issuerCountry").GetString().ShouldBe("US");
        Rows(item.RootElement).ShouldBe([(2027, "Attributing", (string?)"Ordinary shares.")]);

        var list = await InstrumentHttp.Send(HttpMethod.Get, "/instruments", adviser.AccessToken);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var listed = page.RootElement.GetProperty("items").EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == id);
        listed.GetProperty("issuerCountry").GetString().ShouldBe("US");
        Rows(listed).ShouldBe([(2027, "Attributing", (string?)"Ordinary shares.")]);
    }

    [Test]
    public async Task AnInstrumentNobodyClassified_HasNoCountryAndAnEmptyList()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        using var item = await ReadItem(id, tokens.AccessToken);

        item.RootElement.GetProperty("issuerCountry").ValueKind.ShouldBe(JsonValueKind.Null);
        item.RootElement.GetProperty("fifTreatments").ValueKind.ShouldBe(JsonValueKind.Array);
        Rows(item.RootElement).ShouldBeEmpty();
    }

    /// <summary>Sends <paramref name="body"/> for a new instrument of a new TenantAdmin with its current rowVersion.</summary>
    private static async Task<Dictionary<string, string[]>> PutErrors(Dictionary<string, object?> body)
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        body["rowVersion"] = await RowVersion(id, tokens.AccessToken);

        var errors = await Errors(await Put(id, tokens.AccessToken, body));

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("issuerCountry").ValueKind.ShouldBe(JsonValueKind.Null);
        Rows(item.RootElement).ShouldBeEmpty();
        return errors;
    }

    private static Dictionary<string, object?> Body(string? issuerCountry, object[] rows, string? rowVersion) => new()
    {
        ["issuerCountry"] = issuerCountry,
        ["fifTreatments"] = rows,
        ["rowVersion"] = rowVersion
    };

    private static object Row(int taxYear, string treatment, string? note = null) => new { taxYear, treatment, note };

    private static List<(int TaxYear, string Treatment, string? Note)> Rows(JsonElement item) =>
        item.GetProperty("fifTreatments").EnumerateArray()
            .Select(row => (
                row.GetProperty("taxYear").GetInt32(),
                row.GetProperty("treatment").GetString()!,
                row.GetProperty("note").ValueKind == JsonValueKind.Null ? null : row.GetProperty("note").GetString()))
            .ToList();

    private static Task<HttpResponseMessage> Put(Guid id, string? accessToken, object body) =>
        InstrumentHttp.Send(HttpMethod.Put, $"/instruments/{id}/tax-attributes", accessToken, body);

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("errors", out var errors).ShouldBeTrue(content);
        return errors.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    private static async Task<Guid> CreateAsync(string accessToken, string symbol = "VTI", string currency = "USD")
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken,
            new { symbol, name = $"{symbol} Instrument", quoteCurrency = currency });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> RowVersion(Guid id, string accessToken)
    {
        using var item = await ReadItem(id, accessToken);
        return item.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task<JsonDocument> ReadItem(Guid id, string accessToken)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
