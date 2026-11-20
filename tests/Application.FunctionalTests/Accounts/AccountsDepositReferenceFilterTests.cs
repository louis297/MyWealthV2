using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>Deposit matching R37: <c>GET /accounts?depositReference=</c>.</summary>
[Parallelizable]
public class AccountsDepositReferenceFilterTests : TestBase
{
    [Test]
    public async Task ExactReference_ReturnsTheOneAccount_InAnyCase_AndTrimmed()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Other");

        foreach (var value in new[] { world.DepositReference, world.DepositReference.ToLowerInvariant(), $"  {world.DepositReference} " })
        {
            (await IdsAsync(world.TenantAdmin.AccessToken, value)).ShouldBe([world.AccountId], value);
        }
    }

    [TestCase("H2N4R6T80")]
    [TestCase("not-a-reference")]
    [TestCase("K7M3Q9PXC")]
    public async Task UnknownOrInvalidReference_ReturnsAnEmptyList(string value)
    {
        var world = await DepositMatchingWorld.CreateAsync();

        (await IdsAsync(world.TenantAdmin.AccessToken, value)).ShouldBeEmpty();
    }

    [Test]
    public async Task AnotherTenantsReference_ReturnsAnEmptyList()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var world = await DepositMatchingWorld.CreateAsync();

        (await IdsAsync(world.TenantAdmin.AccessToken, south.DepositReference)).ShouldBeEmpty();
    }

    [Test]
    public async Task AnAdviser_DoesNotSeeAnUnassignedCustomersAccount()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bob Adviser", "bob@north-advisory.example", "Password1!", world.Tenant.Id);
        var bob = await FunctionalTestSetup.Oidc.SignInAsync(other.Email, "Password1!", world.Tenant.Code);
        var ann = await world.SignInAdviserAsync();

        (await IdsAsync(bob.AccessToken, world.DepositReference)).ShouldBeEmpty();
        (await IdsAsync(ann.AccessToken, world.DepositReference)).ShouldBe([world.AccountId]);
    }

    private static async Task<List<Guid>> IdsAsync(string accessToken, string depositReference)
    {
        var response = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts?depositReference={Uri.EscapeDataString(depositReference)}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }
}
