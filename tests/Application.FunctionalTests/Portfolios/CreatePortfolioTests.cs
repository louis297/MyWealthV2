using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

public class CreatePortfolioTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", null, new { name = "Balanced" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm P", "firmp");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmp", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmp", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new { name = "Balanced" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_CreatesAndGetReturnsLines()
    {
        var (tenant, tokens) = await Instruments.InstrumentHttp.SignInAdviser("Portfolio Firm", "portfolio-firm");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA");

        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "  Balanced Growth  ",
            baseCurrency = "nzd",
            lines = new[]
            {
                new { instrumentId, targetWeight = 60m },
                new { instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "FPH"), targetWeight = 40m }
            }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var get = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{id}", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var item = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        item.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        item.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Balanced Growth");
        item.RootElement.GetProperty("baseCurrency").GetString().ShouldBe("NZD");
        item.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        item.RootElement.GetProperty("driftAbsolute").GetDecimal().ShouldBe(1m);
        item.RootElement.GetProperty("cashResidualPercent").GetDecimal().ShouldBe(1m);
        item.RootElement.GetProperty("lines").GetArrayLength().ShouldBe(2);
        item.RootElement.GetProperty("lines")[1].GetProperty("targetWeight").GetDecimal().ShouldBe(40m);
    }

    [Test]
    public async Task WeightsThatDoNotSumTo100_Return400()
    {
        var (_, tokens) = await Instruments.InstrumentHttp.SignInAdviser("Sum Firm", "sum-firm");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA");
        var otherId = await CreateInstrumentAsync(tokens.AccessToken, "FPH");

        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "Broken",
            baseCurrency = "NZD",
            lines = new[]
            {
                new { instrumentId, targetWeight = 60m },
                new { instrumentId = otherId, targetWeight = 30m }
            }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task OtherTenant_Returns404()
    {
        var (_, tokens) = await Instruments.InstrumentHttp.SignInAdviser("Owner Firm", "owner-firm", "owner@firm");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA");
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var (_, other) = await Instruments.InstrumentHttp.SignInAdviser("Other Firm", "other-firm", "other@firm");
        var response = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{id}", other.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task List_ReturnsTheEnvelope()
    {
        var (_, tokens) = await Instruments.InstrumentHttp.SignInAdviser("List Firm", "list-firm");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA");
        await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });

        var response = await PortfolioHttp.Send(HttpMethod.Get, "/portfolios", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol)
    {
        var response = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
