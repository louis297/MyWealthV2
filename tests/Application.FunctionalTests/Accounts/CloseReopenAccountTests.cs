using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class CloseReopenAccountTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var close = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{Guid.NewGuid()}/close", accessToken: null,
            new { rowVersion = "x" });
        close.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        close.Headers.Location.ShouldBeNull();

        var reopen = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{Guid.NewGuid()}/reopen", accessToken: null,
            new { rowVersion = "x" });
        reopen.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        reopen.Headers.Location.ShouldBeNull();
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
    public async Task Adviser_CloseReopenAssigned_AreIdempotent204()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var close = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", tokens.AccessToken, new { rowVersion });
        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var closed = await ReadItem(id, tokens.AccessToken);
        closed.RootElement.GetProperty("status").GetString().ShouldBe("Closed");
        closed.RootElement.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        var secondClose = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", tokens.AccessToken,
            new { rowVersion = closed.RootElement.GetProperty("rowVersion").GetString() });
        secondClose.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stillClosed = await ReadItem(id, tokens.AccessToken);
        var reopen = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/reopen", tokens.AccessToken,
            new { rowVersion = stillClosed.RootElement.GetProperty("rowVersion").GetString() });
        reopen.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var opened = await ReadItem(id, tokens.AccessToken);
        opened.RootElement.GetProperty("status").GetString().ShouldBe("Open");
        opened.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        var secondReopen = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/reopen", tokens.AccessToken,
            new { rowVersion = opened.RootElement.GetProperty("rowVersion").GetString() });
        secondReopen.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task TenantAdmin_Close_Returns204()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", tokens.AccessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);

        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", tokens.AccessToken, new { });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", tokens.AccessToken, new { rowVersion = stale });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInTenantAdmin();
        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{Guid.NewGuid()}/close", tokens.AccessToken,
            new { rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }) });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_CloseB_Returns404()
    {
        var (_, _, _, tokensA) = await AccountHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, _, customerB, tokensB) = await AccountHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, customerB.PublicId);
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{idB}/close", tokensA.AccessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_CloseOtherTenant_Returns204()
    {
        var (_, _, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var id = await CreateAsync(adviserTokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, adviserTokens.AccessToken);
        var adminTokens = await AccountHttp.SignInSystemAdmin();

        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", adminTokens.AccessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task CloseReopenGetOnDisabledTenant_StillSucceed()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        await DisableTenantAsync(tenant.Id);

        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);
        (await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var closed = await ReadItem(id, tokens.AccessToken);
        closed.RootElement.GetProperty("status").GetString().ShouldBe("Closed");

        (await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/reopen", tokens.AccessToken,
            new { rowVersion = closed.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task AssertForbidden(Guid id, string accessToken)
    {
        (await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/reopen", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid customerId)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
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
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
