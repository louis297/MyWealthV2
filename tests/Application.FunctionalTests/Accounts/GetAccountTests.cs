using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class GetAccountTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var list = await AccountHttp.Send(HttpMethod.Get, "/accounts", accessToken: null);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.Headers.Location.ShouldBeNull();

        var item = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{Guid.NewGuid()}", accessToken: null);
        item.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        item.Headers.Location.ShouldBeNull();
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

        (await AccountHttp.Send(HttpMethod.Get, "/accounts", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AccountHttp.Send(HttpMethod.Get, $"/accounts/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdminOfA_GetB_Returns404()
    {
        var (_, _, _, tokensA) = await AccountHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, _, customerB, tokensB) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, customerB.PublicId, "South bank");

        var response = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{idB}", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();
        var response = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{Guid.NewGuid()}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_GetOtherTenant_Returns200()
    {
        var (_, _, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var id = await CreateAsync(adviserTokens.AccessToken, customer.PublicId);
        var adminTokens = await AccountHttp.SignInSystemAdmin();

        var response = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{id}", adminTokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Adviser_GetUnassignedCustomerAccount_Returns404()
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

        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_DefaultPaging_ReturnsCurrentTenantOnly()
    {
        var (north, _, customerA, tokensA) = await AccountHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, _, customerB, tokensB) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var bankId = await CreateAsync(tokensA.AccessToken, customerA.PublicId, "Alpha bank", "Bank");
        await CreateAsync(tokensA.AccessToken, customerA.PublicId, "Zulu cash", "Cash");
        var otherId = await CreateAsync(tokensB.AccessToken, customerB.PublicId, "South bank");

        var response = await AccountHttp.Send(HttpMethod.Get, "/accounts", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        ids.ShouldContain(bankId);
        ids.ShouldNotContain(otherId);

        var first = json.RootElement.GetProperty("items")[0];
        first.GetProperty("name").GetString().ShouldBe("Alpha bank");
        first.GetProperty("type").GetString().ShouldBe("Bank");
        first.GetProperty("tenantId").GetGuid().ShouldBe(north.PublicId);
        first.GetProperty("customerId").GetGuid().ShouldBe(customerA.PublicId);
        first.GetProperty("status").GetString().ShouldBe("Open");
        first.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        first.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        first.TryGetProperty("balance", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Adviser_ListOmitsUnassignedCustomer()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var assignedId = await CreateAsync(tokens.AccessToken, customer.PublicId, "Mine");
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, other.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north.example", "Password1!", tenant.Id);
        var adminTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@north.example", "Password1!", "north-advisory");
        var otherId = await CreateAsync(adminTokens.AccessToken, otherCustomer.PublicId, "Theirs");

        var ids = await ReadIds("/accounts", tokens.AccessToken);
        ids.ShouldBe([assignedId]);
        ids.ShouldNotContain(otherId);
    }

    [Test]
    public async Task EnabledOnlyTrue_OmitsClosed()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var openId = await CreateAsync(tokens.AccessToken, customer.PublicId, "Open bank");
        var closedId = await CreateAsync(tokens.AccessToken, customer.PublicId, "Closed other", "Other");
        await CloseAccountAsync(closedId);

        var all = await ReadIds("/accounts", tokens.AccessToken);
        all.ShouldContain(openId);
        all.ShouldContain(closedId);

        var enabledOnly = await ReadIds("/accounts?enabledOnly=true", tokens.AccessToken);
        enabledOnly.ShouldBe([openId]);

        var explicitFalse = await ReadIds("/accounts?enabledOnly=false", tokens.AccessToken);
        explicitFalse.ShouldBe(all, ignoreOrder: true);
    }

    [Test]
    public async Task Search_HitsNameAndPublicId()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var bankId = await CreateAsync(tokens.AccessToken, customer.PublicId, "Everyday spending", "Bank");
        var cashId = await CreateAsync(tokens.AccessToken, customer.PublicId, "Cash tin", "Cash");

        (await ReadIds("/accounts?search=Everyday", tokens.AccessToken)).ShouldBe([bankId]);
        (await ReadIds("/accounts?search=tin", tokens.AccessToken)).ShouldBe([cashId]);
        (await ReadIds($"/accounts?search={bankId}", tokens.AccessToken)).ShouldBe([bankId]);
    }

    [Test]
    public async Task CustomerIdAndTypeFilters()
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, adviser.Id);
        var bankId = await CreateAsync(tokens.AccessToken, customer.PublicId, "Ned bank", "Bank");
        await CreateAsync(tokens.AccessToken, customer.PublicId, "Ned cash", "Cash");
        var otherId = await CreateAsync(tokens.AccessToken, otherCustomer.PublicId, "Pat bank", "Bank");

        (await ReadIds($"/accounts?customerId={customer.PublicId}", tokens.AccessToken))
            .ShouldNotContain(otherId);
        (await ReadIds("/accounts?type=Bank", tokens.AccessToken))
            .ShouldBe([bankId, otherId], ignoreOrder: true);
        (await ReadIds($"/accounts?customerId={customer.PublicId}&type=Bank", tokens.AccessToken))
            .ShouldBe([bankId]);
    }

    [Test]
    public async Task IllegalQuery_Returns400()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();

        (await AccountHttp.Send(HttpMethod.Get, "/accounts?page=0", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AccountHttp.Send(HttpMethod.Get, "/accounts?pageSize=101", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AccountHttp.Send(HttpMethod.Get, "/accounts?enabledOnly=maybe", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AccountHttp.Send(HttpMethod.Get, "/accounts?type=Vault", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemAdmin_ListWithoutTenantId_Returns400()
    {
        var tokens = await AccountHttp.SignInSystemAdmin();
        var response = await AccountHttp.Send(HttpMethod.Get, "/accounts", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemAdmin_ListWithTenantId_ReturnsThatTenant()
    {
        var (north, _, customer, adviserTokens) = await AccountHttp.SignInAdviser(
            "North Advisory", "north-advisory", "ann@north.example");
        var id = await CreateAsync(adviserTokens.AccessToken, customer.PublicId);
        var adminTokens = await AccountHttp.SignInSystemAdmin();

        var response = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts?tenantId={north.PublicId}", adminTokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([id]);
    }

    [Test]
    public async Task TenantAdmin_ListWithTenantId_Returns400()
    {
        var (tenant, _, _, tokens) = await AccountHttp.SignInTenantAdmin();
        var response = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts?tenantId={tenant.PublicId}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetAndListOnDisabledTenant_StillReturn200()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        await DisableTenantAsync(tenant.Id);

        (await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AccountHttp.Send(HttpMethod.Get, "/accounts", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateAsync(
        string accessToken,
        Guid customerId,
        string name = "Everyday spending",
        string type = "Bank",
        string currency = "NZD")
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type,
            currency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ReadIds(string path, string accessToken)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task CloseAccountAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == publicId);
        account.Close();
        await db.SaveChangesAsync();
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
