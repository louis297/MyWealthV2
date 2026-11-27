using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Transactions.ExternalDividendTests;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>External income R1, R10: another tenant's Account or Instrument, and an Adviser's unassigned Customer,
/// are 404 on <c>POST /transactions/dividend</c>, before any rule of the body.</summary>
[Parallelizable]
public class ExternalDividendIsolationTests : TestBase
{
    [Test]
    public async Task AnotherTenantsAccount_Returns404_AndWritesNothing()
    {
        var north = await WorldAsync("NZD", "NZD");
        var south = await WorldAsync("NZD", "NZD", tenantCode: "south-advisory");
        var body = ExampleA(north);
        body["accountId"] = south.AccountId;

        (await PostAsync(north.Token, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Before the rules that need the Account (R3): without a bookedAt it is still a 404.
        body.Remove("bookedAt");
        (await PostAsync(north.Token, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustodialHttp.TransactionCountAsync(south.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task AnotherTenantsInstrument_Returns404()
    {
        var north = await WorldAsync("NZD", "NZD");
        var south = await WorldAsync("NZD", "NZD", tenantCode: "south-advisory");
        var body = ExampleA(north);
        body["instrumentId"] = south.InstrumentId;

        (await PostAsync(north.Token, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body["instrumentId"] = Guid.NewGuid();
        (await PostAsync(north.Token, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustodialHttp.TransactionCountAsync(north.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task TheSystemAdmin_NamingAnotherTenant_GetsA404ForThisTenantsAccount()
    {
        var north = await WorldAsync("NZD", "NZD");
        var south = await WorldAsync("NZD", "NZD", tenantCode: "south-advisory");
        var system = await AccountHttp.SignInSystemAdmin();
        var body = ExampleA(north);
        body["tenantId"] = south.TenantPublicId;

        (await PostAsync(system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AnAdvisersUnassignedCustomer_Returns404()
    {
        var world = await WorldAsync("NZD", "NZD");
        var bob = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bob Adviser", "bob@north-advisory.example", "Password1!", world.TenantId);
        var bobTokens = await FunctionalTestSetup.Oidc.SignInAsync("bob@north-advisory.example", "Password1!", "north-advisory");

        (await PostAsync(bobTokens.AccessToken, ExampleA(world))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
        bob.Id.ShouldBeGreaterThan(0);
    }
}
