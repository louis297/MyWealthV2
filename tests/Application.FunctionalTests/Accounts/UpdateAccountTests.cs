using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class UpdateAccountTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{Guid.NewGuid()}", accessToken: null,
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

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{Guid.NewGuid()}", tokens.AccessToken,
            new { name = "Renamed", rowVersion = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_RenamesAssigned_Returns204()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Renamed everyday",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Renamed everyday");
        item.RootElement.GetProperty("type").GetString().ShouldBe("Bank");
        item.RootElement.GetProperty("currency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task Adviser_Unassigned_Returns404()
    {
        var (tenant, _, _, tokens) = await AccountHttp.SignInAdviser();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, other.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north.example", "Password1!", tenant.Id);
        var adminTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@north.example", "Password1!", "north-advisory");
        var id = await CreateAsync(adminTokens.AccessToken, otherCustomer.PublicId);
        var rowVersion = await ReadRowVersion(id, adminTokens.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_RenamesName_Returns204()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Everyday renamed",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Everyday renamed");
    }

    [Test]
    public async Task PutWithType_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                type = "Cash",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithCurrency_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                currency = "USD",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithStatusOrIsActive_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        (await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                status = "Closed",
                rowVersion
            })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                isActive = false,
                rowVersion
            })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new { name = "Renamed" });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Renamed",
                rowVersion = stale
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInTenantAdmin();
        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Renamed",
                rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_PutB_Returns404()
    {
        var (_, _, _, tokensA) = await AccountHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, _, customerB, tokensB) = await AccountHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, customerB.PublicId);
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{idB}", tokensA.AccessToken, new
            {
                name = "Renamed",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_PutOtherTenant_Returns204()
    {
        var (_, _, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var id = await CreateAsync(adviserTokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadRowVersion(id, adviserTokens.AccessToken);
        var adminTokens = await AccountHttp.SignInSystemAdmin();

        var response = await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", adminTokens.AccessToken, new
            {
                name = "Renamed by admin",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
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
}
