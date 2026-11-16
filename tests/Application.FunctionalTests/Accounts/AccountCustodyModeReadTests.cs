using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>Account custody mode R7 and R8: the item carries <c>custodyMode</c>, and the list filters on it.</summary>
public class AccountCustodyModeReadTests : TestBase
{
    [Test]
    public async Task Get_ItemCarriesCustodyMode()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var id = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId);

        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("custodyMode").GetString().ShouldBe("External");
    }

    [Test]
    public async Task List_ItemsCarryCustodyMode()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Ned bank");
        await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Ned cash", "Cash");

        var items = await ReadItemsAsync("/accounts", tokens.AccessToken);

        items.Count.ShouldBe(2);
        items.ShouldAllBe(item => item.CustodyMode == "External");
    }

    [Test]
    public async Task Customer_ReadingOwnAccount_GetsCustodyMode()
    {
        var (_, _, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var id = await AccountHttp.OpenAccountAsync(adviserTokens.AccessToken, customer.PublicId);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("custodyMode").GetString().ShouldBe("External");

        (await ReadItemsAsync("/accounts", tokens.AccessToken))
            .ShouldBe([(id, "External")]);
    }

    [Test]
    public async Task CustodyModeFilter_ExternalReturnsTheCallersAccounts_CustodialReturnsAnEmptyPage()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var bankId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Ned bank");
        var cashId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Ned cash", "Cash");

        (await ReadItemsAsync("/accounts?custodyMode=External", tokens.AccessToken))
            .Select(item => item.Id)
            .ShouldBe([bankId, cashId], ignoreOrder: true);

        var custodial = await AccountHttp.Send(HttpMethod.Get, "/accounts?custodyMode=Custodial", tokens.AccessToken);
        custodial.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await custodial.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(0);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [TestCase("nope")]
    [TestCase("external")]
    [TestCase("CUSTODIAL")]
    [TestCase("1")]
    public async Task CustodyModeFilter_NotAnExactName_Returns400(string value)
    {
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts?custodyMode={value}", tokens.AccessToken);

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => property.Name)
            .ShouldContain(name => string.Equals(name, "CustodyMode", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task CustodyModeFilter_CombinesWithCustomerIdAndType()
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, adviser.Id);
        var bankId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Ned bank");
        await AccountHttp.OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Ned cash", "Cash");
        var otherId = await AccountHttp.OpenAccountAsync(tokens.AccessToken, otherCustomer.PublicId, "Pat bank");

        (await ReadItemsAsync($"/accounts?custodyMode=External&customerId={customer.PublicId}&type=Bank", tokens.AccessToken))
            .Select(item => item.Id)
            .ShouldBe([bankId]);
        (await ReadItemsAsync("/accounts?custodyMode=External&type=Bank", tokens.AccessToken))
            .Select(item => item.Id)
            .ShouldBe([bankId, otherId], ignoreOrder: true);
        (await ReadItemsAsync($"/accounts?custodyMode=Custodial&customerId={customer.PublicId}", tokens.AccessToken))
            .ShouldBeEmpty();
    }

    [Test]
    public async Task CustodyModeFilter_TenantAdminOfA_SeesNoAccountOfTenantB()
    {
        var (_, _, customerA, tokensA) = await AccountHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, _, customerB, tokensB) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var idA = await AccountHttp.OpenAccountAsync(tokensA.AccessToken, customerA.PublicId, "North bank");
        await AccountHttp.OpenAccountAsync(tokensB.AccessToken, customerB.PublicId, "South bank");

        var page = await AccountHttp.Send(HttpMethod.Get, "/accounts?custodyMode=External", tokensA.AccessToken);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([idA]);

        (await ReadItemsAsync($"/accounts?custodyMode=External&customerId={customerB.PublicId}", tokensA.AccessToken))
            .ShouldBeEmpty();
    }

    private static async Task<List<(Guid Id, string? CustodyMode)>> ReadItemsAsync(string path, string accessToken)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (
                item.GetProperty("id").GetGuid(),
                item.TryGetProperty("custodyMode", out var mode) ? mode.GetString() : null))
            .ToList();
    }
}
