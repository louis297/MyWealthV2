using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

/// <summary>Instrument-tax-attributes R1, R2, R21: two tenants and their tax attributes.</summary>
[Parallelizable]
public class InstrumentTaxAttributesIsolationTests : TestBase
{
    [Test]
    public async Task ATenantAdminsPutOnAnotherTenantsInstrument_Returns404_AndNothingChanges()
    {
        var (_, north) = await InstrumentHttp.SignInTenantAdmin("North Advisory", "north-advisory", "tess@north.example");
        var (_, south) = await InstrumentHttp.SignInTenantAdmin("South Co", "south-co", "tess@south.example");
        var southId = await CreateAsync(south.AccessToken);
        var rowVersion = await RowVersion(southId, south.AccessToken);

        var response = await Put(southId, north.AccessToken, "US", rowVersion);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var item = await ReadItem(southId, south.AccessToken);
        item.RootElement.GetProperty("issuerCountry").ValueKind.ShouldBe(JsonValueKind.Null);
        item.RootElement.GetProperty("fifTreatments").EnumerateArray().ShouldBeEmpty();
        item.RootElement.GetProperty("rowVersion").GetString().ShouldBe(rowVersion);
    }

    [Test]
    public async Task ASystemAdminsPutOnAnyTenantsInstrument_Returns204()
    {
        var (_, south) = await InstrumentHttp.SignInTenantAdmin("South Co", "south-co", "tess@south.example");
        var southId = await CreateAsync(south.AccessToken);
        var systemAdmin = await InstrumentHttp.SignInSystemAdmin();

        var response = await Put(southId, systemAdmin.AccessToken, "US", await RowVersion(southId, south.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await ReadItem(southId, south.AccessToken);
        item.RootElement.GetProperty("issuerCountry").GetString().ShouldBe("US");
    }

    [Test]
    public async Task TwoTenants_ClassifyTheSameSymbolDifferently_AndEachListShowsOnlyItsOwnRows()
    {
        var (_, north) = await InstrumentHttp.SignInTenantAdmin("North Advisory", "north-advisory", "tess@north.example");
        var (_, south) = await InstrumentHttp.SignInTenantAdmin("South Co", "south-co", "tess@south.example");
        var northId = await CreateAsync(north.AccessToken);
        var southId = await CreateAsync(south.AccessToken);
        (await Put(northId, north.AccessToken, "US", await RowVersion(northId, north.AccessToken), "Attributing"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Put(southId, south.AccessToken, "US", await RowVersion(southId, south.AccessToken), "AttributingNoFdr"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListedTreatments(north.AccessToken)).ShouldBe([(northId, "Attributing")]);
        (await ListedTreatments(south.AccessToken)).ShouldBe([(southId, "AttributingNoFdr")]);
    }

    [Test]
    public async Task ADisabledTenantsTenantAdmin_Returns403()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await RowVersion(id, tokens.AccessToken);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Tenants.SingleAsync(row => row.Id == tenant.Id);
            stored.Disable();
            await db.SaveChangesAsync();
        }

        var response = await Put(id, tokens.AccessToken, "US", rowVersion);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<List<(Guid Id, string Treatment)>> ListedTreatments(string accessToken)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Get, "/instruments", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return page.RootElement.GetProperty("items").EnumerateArray()
            .SelectMany(item => item.GetProperty("fifTreatments").EnumerateArray()
                .Select(row => (item.GetProperty("id").GetGuid(), row.GetProperty("treatment").GetString()!)))
            .ToList();
    }

    private static Task<HttpResponseMessage> Put(Guid id, string accessToken, string country, string rowVersion, string treatment = "Attributing") =>
        InstrumentHttp.Send(HttpMethod.Put, $"/instruments/{id}/tax-attributes", accessToken, new
        {
            issuerCountry = country,
            fifTreatments = new[] { new { taxYear = 2027, treatment, note = (string?)null } },
            rowVersion
        });

    private static async Task<Guid> CreateAsync(string accessToken)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken,
            new { symbol = "VTI", name = "Vanguard Total Stock Market ETF", quoteCurrency = "USD" });
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
