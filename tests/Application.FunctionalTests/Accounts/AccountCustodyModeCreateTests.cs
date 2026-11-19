using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>
/// Account custody mode R3: <c>POST /accounts</c> requires <c>custodyMode</c>. Custody modes R2 to R4: it accepts
/// External with any openable type and Custodial with Brokerage only.
/// </summary>
[Parallelizable]
public class AccountCustodyModeCreateTests : TestBase
{
    [Test]
    public async Task Adviser_External_Returns201AndGetReturnsExternal()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "External"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await ReadCustodyModeAsync(response, tokens.AccessToken)).ShouldBe("External");
    }

    [Test]
    public async Task TenantAdmin_WithoutCustodyMode_Returns400AndWritesNothing()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
        });

        await AssertCustodyModeErrorAsync(response);
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);
    }

    [Test]
    public async Task Adviser_NullCustodyMode_Returns400AndWritesNothing()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, null));

        await AssertCustodyModeErrorAsync(response);
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);
    }

    [TestCase("external")]
    [TestCase("custodial")]
    [TestCase("nope")]
    [TestCase("0")]
    public async Task Adviser_NotAnExactCustodyModeName_Returns400AndWritesNothing(string custodyMode)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, custodyMode));

        await AssertCustodyModeErrorAsync(response);
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);
    }

    [Test]
    public async Task Adviser_CustodialBrokerage_Returns201AndGetReturnsCustodial()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        await ClientMoneyHttp.EnsureActiveForCustomerAsync(customer.PublicId, "NZD");

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "Custodial", "Brokerage"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await ReadCustodyModeAsync(response, tokens.AccessToken)).ShouldBe("Custodial");
    }

    [Test]
    public async Task TenantAdmin_CustodialBrokerage_Returns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await ClientMoneyHttp.EnsureActiveForCustomerAsync(customer.PublicId, "NZD");

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "Custodial", "Brokerage"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await ReadCustodyModeAsync(response, tokens.AccessToken)).ShouldBe("Custodial");
    }

    [TestCase("Bank")]
    [TestCase("Cash")]
    [TestCase("Other")]
    public async Task Adviser_CustodialWithATypeOtherThanBrokerage_Returns400OnTypeAndWritesNothing(string type)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "Custodial", type));

        await AssertCustodialTypeErrorAsync(response);
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);
    }

    [TestCase("Bank")]
    [TestCase("Cash")]
    [TestCase("Other")]
    public async Task TenantAdmin_CustodialWithATypeOtherThanBrokerage_Returns400OnType(string type)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "Custodial", type));

        await AssertCustodialTypeErrorAsync(response);
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);
    }

    [TestCase("Bank")]
    [TestCase("Cash")]
    [TestCase("Brokerage")]
    [TestCase("Other")]
    public async Task Adviser_ExternalWithEachOpenableType_Returns201(string type)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "External", type));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task SystemAdmin_FollowsTheSameRules()
    {
        var (tenant, _, customer, _) = await AccountHttp.SignInAdviser();
        var tokens = await AccountHttp.SignInSystemAdmin();

        await AssertCustodyModeErrorAsync(await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
        }));
        await AssertCustodialTypeErrorAsync(await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            custodyMode = "Custodial",
            currency = "NZD"
        }));
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);

        await ClientMoneyHttp.EnsureActiveAsync(tenant.Id, "NZD");
        var custodial = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Managed portfolio",
            type = "Brokerage",
            custodyMode = "Custodial",
            currency = "NZD"
        });
        custodial.StatusCode.ShouldBe(HttpStatusCode.Created, await custodial.Content.ReadAsStringAsync());
        (await ReadCustodyModeAsync(custodial, tokens.AccessToken)).ShouldBe("Custodial");

        var created = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            custodyMode = "External",
            currency = "NZD"
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await ReadCustodyModeAsync(created, tokens.AccessToken)).ShouldBe("External");
    }

    private static object Body(Guid customerId, string? custodyMode, string type = "Bank") => new
    {
        customerId,
        name = "Everyday spending",
        type,
        custodyMode,
        currency = "NZD"
    };

    private static async Task AssertCustodialTypeErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("code", out _).ShouldBeFalse();
        var type = json.RootElement.GetProperty("errors").EnumerateObject()
            .Single(property => string.Equals(property.Name, "Type", StringComparison.OrdinalIgnoreCase));
        type.Value.EnumerateArray().Select(message => message.GetString())
            .ShouldContain("A Custodial account must be a Brokerage account.");
    }

    private static async Task AssertCustodyModeErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("code", out _).ShouldBeFalse();
        json.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => property.Name)
            .ShouldContain(name => string.Equals(name, "CustodyMode", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> ReadCustodyModeAsync(HttpResponseMessage created, string accessToken)
    {
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", accessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var item = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        return item.RootElement.GetProperty("custodyMode").GetString();
    }

    private static async Task<int> CountAccountsAsync(int customerId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Accounts.CountAsync(row => row.CustomerId == customerId);
    }
}
