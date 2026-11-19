using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>Client money accounts R20–R22: <c>GET /accounts/{id}/deposit-instructions</c>.</summary>
[Parallelizable]
public class DepositInstructionsTests : TestBase
{
    private const string Number = "02-0100-0123456-00";

    [Test]
    public async Task OpenCustodialAccountWithACoveredCurrency_Returns200_ForEveryoneWhoCanReadIt()
    {
        var world = await ArrangeAsync();
        var system = await AccountHttp.SignInSystemAdmin();
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", "north-advisory");
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);
        var reference = await ReferenceAsync(world.AdminToken, world.Custodial);

        foreach (var token in new[] { world.AdminToken, adviser.AccessToken, customer.AccessToken, system.AccessToken })
        {
            var body = await InstructionsAsync(token, world.Custodial);

            body.GetProperty("accountId").GetGuid().ShouldBe(world.Custodial);
            body.GetProperty("currency").GetString().ShouldBe("USD");
            body.GetProperty("reference").GetString().ShouldBe(reference);
            body.GetProperty("bankName").GetString().ShouldBe("Simulator Bank");
            body.GetProperty("accountName").GetString().ShouldBe("North Advisory Ltd Client Funds Trust Account");
            body.GetProperty("accountNumber").GetString().ShouldBe(Number);
            body.EnumerateObject().Select(property => property.Name).ShouldBe(
                ["accountId", "currency", "reference", "bankName", "accountName", "accountNumber"], ignoreOrder: true);
        }
    }

    [Test]
    public async Task ExternalAccount_Returns404WithoutACode()
    {
        var world = await ArrangeAsync();
        var external = await AccountHttp.OpenAccountAsync(world.AdminToken, world.Customer.PublicId, currency: "USD");

        await AssertNotFoundAsync(world.AdminToken, external);
    }

    [Test]
    public async Task ClosedAccount_Returns404()
    {
        var world = await ArrangeAsync();
        (await CloseAsync(world.AdminToken, world.Custodial)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await AssertNotFoundAsync(world.AdminToken, world.Custodial);
    }

    [Test]
    public async Task DisabledClientMoneyAccount_Returns404_ThenANewRowReturnsItsDetails()
    {
        var world = await ArrangeAsync();
        var system = await AccountHttp.SignInSystemAdmin();
        (await ClientMoneyHttp.SetActiveAsync(system.AccessToken, world.Tenant.PublicId, world.Row, active: false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await AssertNotFoundAsync(world.AdminToken, world.Custodial);

        await ClientMoneyHttp.CreateAsync(system.AccessToken, world.Tenant.PublicId,
            ClientMoneyHttp.Body("USD", "12-3456-7890123-00", "Second Bank", "North Advisory Ltd Trust Account No 2"));
        var body = await InstructionsAsync(world.AdminToken, world.Custodial);
        body.GetProperty("bankName").GetString().ShouldBe("Second Bank");
        body.GetProperty("accountName").GetString().ShouldBe("North Advisory Ltd Trust Account No 2");
        body.GetProperty("accountNumber").GetString().ShouldBe("12-3456-7890123-00");
    }

    [Test]
    public async Task CallersWhoCannotSeeTheAccount_Get404()
    {
        var world = await ArrangeAsync();
        await TestApp.CreatePersonAsync(UserRole.Adviser, "Una Unassigned", "una@north.example", "Password1!", world.Tenant.Id);
        var unassigned = await FunctionalTestSetup.Oidc.SignInAsync("una@north.example", "Password1!", "north-advisory");
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Olive Other", "olive@north.example", "Password1!", world.Tenant.Id, world.Adviser.Id);
        var otherCustomerToken = await TestApp.IssueAccessTokenAsync(otherCustomer);
        var (_, _, _, southAdmin) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tom@south.example",
            adviserEmail: "sam@south.example", customerEmail: "sue@south.example");

        await AssertNotFoundAsync(unassigned.AccessToken, world.Custodial);
        await AssertNotFoundAsync(otherCustomerToken.AccessToken, world.Custodial);
        await AssertNotFoundAsync(southAdmin.AccessToken, world.Custodial);
        await AssertNotFoundAsync(world.AdminToken, Guid.NewGuid());
    }

    [Test]
    public async Task WithoutBearer_Returns401()
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{Guid.NewGuid()}/deposit-instructions", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    private sealed record World(
        Domain.Entities.Tenant Tenant,
        Domain.Entities.User Adviser,
        Domain.Entities.User Customer,
        string AdminToken,
        Guid Row,
        Guid Custodial);

    private static async Task<World> ArrangeAsync()
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var system = await AccountHttp.SignInSystemAdmin();
        var row = await ClientMoneyHttp.CreateAsync(system.AccessToken, tenant.PublicId, ClientMoneyHttp.Body("USD", Number));
        var custodial = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        return new World(tenant, adviser, customer, tokens.AccessToken, row, custodial);
    }

    private static async Task<JsonElement> InstructionsAsync(string token, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/deposit-instructions", token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    private static async Task AssertNotFoundAsync(string token, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/deposit-instructions", token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, content);
        if (content.Length > 0)
        {
            using var json = JsonDocument.Parse(content);
            json.RootElement.TryGetProperty("code", out _).ShouldBeFalse(content);
        }
    }

    private static async Task<string> ReferenceAsync(string token, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("depositReference").GetString()!;
    }
}
