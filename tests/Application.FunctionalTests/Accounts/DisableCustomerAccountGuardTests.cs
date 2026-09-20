using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Users.Customers;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class DisableCustomerAccountGuardTests : TestBase
{
    [Test]
    public async Task DisableCustomer_WithOpenAccount_Returns400OrdinaryErrors()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await CreateAsync(tokens.AccessToken, customer.PublicId);
        var rowVersion = await ReadCustomerRowVersion(customer.PublicId, tokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{customer.PublicId}/disable", tokens.AccessToken,
            new { rowVersion });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("errors", out _).ShouldBeTrue();
        if (json.RootElement.TryGetProperty("code", out var code))
        {
            code.GetString().ShouldNotBe("disabled");
        }
    }

    [Test]
    public async Task DisableCustomer_WithOnlyClosedAccounts_Returns204()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        await CloseAsync(id, tokens.AccessToken);

        var rowVersion = await ReadCustomerRowVersion(customer.PublicId, tokens.AccessToken);
        var response = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{customer.PublicId}/disable", tokens.AccessToken,
            new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getCustomer = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{customer.PublicId}", tokens.AccessToken);
        getCustomer.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var person = JsonDocument.Parse(await getCustomer.Content.ReadAsStringAsync());
        person.RootElement.GetProperty("status").GetString().ShouldBe("disabled");
        person.RootElement.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Reopen_OnDisabledCustomer_Returns400DisabledUser()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        await CloseAsync(id, tokens.AccessToken);

        var customerRowVersion = await ReadCustomerRowVersion(customer.PublicId, tokens.AccessToken);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{customer.PublicId}/disable", tokens.AccessToken,
            new { rowVersion = customerRowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadAccount(id, tokens.AccessToken);
        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/reopen", tokens.AccessToken,
            new { rowVersion = item.RootElement.GetProperty("rowVersion").GetString() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(customer.PublicId);
    }

    [Test]
    public async Task GetAndRename_OnDisabledCustomer_StillSucceed()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, customer.PublicId);
        await CloseAsync(id, tokens.AccessToken);

        var customerRowVersion = await ReadCustomerRowVersion(customer.PublicId, tokens.AccessToken);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{customer.PublicId}/disable", tokens.AccessToken,
            new { rowVersion = customerRowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var item = await ReadAccount(id, tokens.AccessToken);
        (await AccountHttp.Send(
            HttpMethod.Put, $"/accounts/{id}", tokens.AccessToken, new
            {
                name = "Still visible",
                rowVersion = item.RootElement.GetProperty("rowVersion").GetString()
            }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
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

    private static async Task CloseAsync(Guid id, string accessToken)
    {
        using var item = await ReadAccount(id, accessToken);
        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{id}/close", accessToken,
            new { rowVersion = item.RootElement.GetProperty("rowVersion").GetString() });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<string> ReadCustomerRowVersion(Guid customerId, string accessToken)
    {
        var response = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{customerId}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task<JsonDocument> ReadAccount(Guid id, string accessToken)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
