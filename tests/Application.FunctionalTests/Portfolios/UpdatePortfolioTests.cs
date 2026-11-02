using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

public class UpdatePortfolioTests : TestBase
{
    [Test]
    public async Task Rename_Returns204AndKeepsWeights()
    {
        var (tokens, id, rowVersion, instrumentId) = await CreateAsync("Rename Firm", "rename-firm");

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{id}", tokens.AccessToken, new
        {
            name = "Growth",
            rowVersion,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var get = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{id}", tokens.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("name").GetString().ShouldBe("Growth");
        json.RootElement.GetProperty("lines")[0].GetProperty("targetWeight").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task DifferentBaseCurrency_Returns400()
    {
        var (tokens, id, rowVersion, instrumentId) = await CreateAsync("Currency Firm", "currency-firm");

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{id}", tokens.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "USD",
            rowVersion,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (tokens, id, _, instrumentId) = await CreateAsync("Version Firm", "version-firm");

        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{id}", tokens.AccessToken, new
        {
            name = "Growth",
            rowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]),
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Disable_MarksInactive()
    {
        var (tokens, id, rowVersion, _) = await CreateAsync("Disable Firm", "disable-firm");

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/portfolios/{id}/disable", tokens.AccessToken, new
        {
            rowVersion
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var get = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{id}", tokens.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    private static async Task<(TokenResponse Tokens, Guid Id, string RowVersion, Guid InstrumentId)> CreateAsync(
        string tenantName,
        string tenantCode)
    {
        var (_, tokens) = await Instruments.InstrumentHttp.SignInAdviser(tenantName, tenantCode, tenantCode + "@firm");
        var createdInstrument = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            symbol = "AIA",
            name = "AIA fund",
            quoteCurrency = "NZD"
        });
        using var instrumentJson = JsonDocument.Parse(await createdInstrument.Content.ReadAsStringAsync());
        var instrumentId = instrumentJson.RootElement.GetProperty("id").GetGuid();
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "Balanced",
            baseCurrency = "NZD",
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("id").GetGuid();
        var get = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{id}", tokens.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        return (tokens, id, json.RootElement.GetProperty("rowVersion").GetString()!, instrumentId);
    }
}
