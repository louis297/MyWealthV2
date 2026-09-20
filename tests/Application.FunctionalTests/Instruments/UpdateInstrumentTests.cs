using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

public class UpdateInstrumentTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{Guid.NewGuid()}", accessToken: null,
            new { name = "Renamed", rowVersion = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{Guid.NewGuid()}", tokens.AccessToken,
            new { name = "Renamed", rowVersion = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", tokens.AccessToken,
            new { name = "Renamed", rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_RenamesNameAndSymbol_Returns204()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", tokens.AccessToken, new
            {
                symbol = "vti-us",
                name = "Vanguard Total US",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("symbol").GetString().ShouldBe("VTI-US");
        item.RootElement.GetProperty("name").GetString().ShouldBe("Vanguard Total US");
        item.RootElement.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task PutWithQuoteCurrency_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                quoteCurrency = "NZD",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithTenantId_Returns400()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                tenantId = tenant.PublicId,
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithIsEnabled_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                isEnabled = false,
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", tokens.AccessToken, new { name = "Renamed" });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                rowVersion = stale
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Renamed",
                rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_PutB_Returns404()
    {
        var (_, tokensA) = await InstrumentHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await InstrumentHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example");
        var idB = await CreateAsync(tokensB.AccessToken);
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{idB}", tokensA.AccessToken, new
            {
                name = "Renamed",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_PutOtherTenant_Returns204()
    {
        var (_, adviserTokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateAsync(adviserTokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, adviserTokens.AccessToken);
        var adminTokens = await InstrumentHttp.SignInSystemAdmin();

        var response = await InstrumentHttp.Send(
            HttpMethod.Put, $"/instruments/{id}", adminTokens.AccessToken, new
            {
                name = "Renamed by admin",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<Guid> CreateAsync(string accessToken)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol = "VTI",
            name = "Vanguard Total Stock Market ETF",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> ReadRowVersion(Guid id, string accessToken)
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
