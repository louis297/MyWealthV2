using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

public class DisableEnableInstrumentTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var disable = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{Guid.NewGuid()}/disable", accessToken: null,
            new { rowVersion = "x" });
        disable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        disable.Headers.Location.ShouldBeNull();

        var enable = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{Guid.NewGuid()}/enable", accessToken: null,
            new { rowVersion = "x" });
        enable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        enable.Headers.Location.ShouldBeNull();
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
        await AssertForbidden(Guid.NewGuid(), tokens.AccessToken);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken);
        await AssertForbidden(id, tokens.AccessToken);
    }

    [Test]
    public async Task TenantAdmin_DisableEnable_AreIdempotent204()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var disable = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, tokens.AccessToken);
        disabled.RootElement.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        var secondDisable = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/disable", tokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() });
        secondDisable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stillDisabled = await ReadItem(id, tokens.AccessToken);
        var enable = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/enable", tokens.AccessToken,
            new { rowVersion = stillDisabled.RootElement.GetProperty("rowVersion").GetString() });
        enable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var enabled = await ReadItem(id, tokens.AccessToken);
        enabled.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        var secondEnable = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/enable", tokens.AccessToken,
            new { rowVersion = enabled.RootElement.GetProperty("rowVersion").GetString() });
        secondEnable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/disable", tokens.AccessToken, new { });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/disable", tokens.AccessToken, new { rowVersion = stale });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var response = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{Guid.NewGuid()}/disable", tokens.AccessToken,
            new { rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }) });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_DisableB_Returns404()
    {
        var (_, tokensA) = await InstrumentHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await InstrumentHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example");
        var idB = await CreateAsync(tokensB.AccessToken);
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{idB}/disable", tokensA.AccessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_DisableOtherTenant_Returns204()
    {
        var (_, adviserTokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateAsync(adviserTokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, adviserTokens.AccessToken);
        var adminTokens = await InstrumentHttp.SignInSystemAdmin();

        var response = await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/disable", adminTokens.AccessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task AssertForbidden(Guid id, string accessToken)
    {
        (await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/disable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await InstrumentHttp.Send(
            HttpMethod.Post, $"/instruments/{id}/enable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
