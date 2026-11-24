using System.Net;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R1, R17, R52: the standing reinvestment election of an Account.</summary>
[Parallelizable]
public class DividendElectionTests : TestBase
{
    private static string Path(Guid accountId) => $"/accounts/{accountId}/dividend-reinvestment";

    private static async Task<HttpResponseMessage> SetAsync(string token, Guid accountId, bool reinvest, string? rowVersion = null)
    {
        rowVersion ??= (await AccountClosureHttp.AccountAsync(token, accountId)).GetProperty("rowVersion").GetString();
        return await AccountHttp.Send(HttpMethod.Put, Path(accountId), token, new { reinvest, rowVersion });
    }

    [Test]
    public async Task ATenantAdmin_AndTheAdviser_SetTheElection_AndTheAccountItemShowsIt()
    {
        var world = await NominatedWorld.CreateAsync();
        var account = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);

        (await SetAsync(world.TenantAdmin.AccessToken, account, true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AccountClosureHttp.AccountAsync(world.AdviserToken.AccessToken, account)).GetProperty("reinvestDividends")
            .GetBoolean().ShouldBeTrue();

        (await SetAsync(world.AdviserToken.AccessToken, account, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AccountClosureHttp.AccountAsync(world.CustomerToken.AccessToken, account)).GetProperty("reinvestDividends")
            .GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task AnExternalAccount_Returns400AtAccountId()
    {
        var world = await NominatedWorld.CreateAsync();
        var account = await CustodialHttp.OpenExternalAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);

        (await CustodialHttp.ErrorsAsync(await SetAsync(world.TenantAdmin.AccessToken, account, true)))["AccountId"]
            .ShouldBe(["Dividends are reinvested only on a Custodial account."]);
    }

    [Test]
    public async Task ACustomer_Gets403_AStaleRowVersion409_AndAnotherTenantsAccount404()
    {
        var world = await NominatedWorld.CreateAsync();
        var account = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var southAccount = await CustodialHttp.OpenCustodialAsync(south.TenantAdmin.AccessToken, south.Customer.PublicId);
        var rowVersion = (await AccountClosureHttp.AccountAsync(world.TenantAdmin.AccessToken, account)).GetProperty("rowVersion").GetString();

        (await AccountHttp.Send(HttpMethod.Put, Path(account), world.CustomerToken.AccessToken, new { reinvest = true, rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SetAsync(world.TenantAdmin.AccessToken, account, true, "AAAAAAAAAAE=")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AccountHttp.Send(HttpMethod.Put, Path(southAccount), world.TenantAdmin.AccessToken, new { reinvest = true, rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AccountHttp.Send(HttpMethod.Put, Path(account), null, new { reinvest = true, rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdatingTheAccount_DoesNotTakeTheElection()
    {
        var world = await NominatedWorld.CreateAsync();
        var account = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);
        var rowVersion = (await AccountClosureHttp.AccountAsync(world.TenantAdmin.AccessToken, account)).GetProperty("rowVersion").GetString();

        var response = await AccountHttp.Send(HttpMethod.Put, $"/accounts/{account}", world.TenantAdmin.AccessToken,
            new { name = "Renamed", rowVersion, reinvestDividends = true });

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("ReinvestDividends");
    }
}
