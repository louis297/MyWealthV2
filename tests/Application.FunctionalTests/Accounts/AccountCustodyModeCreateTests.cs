using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>Account custody mode R3 to R5: <c>POST /accounts</c> requires <c>custodyMode</c> and accepts only External.</summary>
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

    [TestCase("Custodial")]
    [TestCase("external")]
    [TestCase("nope")]
    [TestCase("0")]
    public async Task Adviser_NotExternal_Returns400AndWritesNothing(string custodyMode)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, custodyMode));

        await AssertCustodyModeErrorAsync(response);
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);
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
        await AssertCustodyModeErrorAsync(await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            tenantId = tenant.PublicId,
            customerId = customer.PublicId,
            name = "Everyday spending",
            type = "Bank",
            custodyMode = "Custodial",
            currency = "NZD"
        }));
        (await CountAccountsAsync(customer.Id)).ShouldBe(0);

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

    private static object Body(Guid customerId, string? custodyMode) => new
    {
        customerId,
        name = "Everyday spending",
        type = "Bank",
        custodyMode,
        currency = "NZD"
    };

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
