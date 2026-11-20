using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Deposit matching R35: nobody enters a <c>TransferIn</c> on a Custodial account by hand.</summary>
[Parallelizable]
public class CustodialTransferInRefusedTests : TestBase
{
    private const string Message =
        "Deposits to a Custodial account come from the bank statement. The customer pays in with the account's deposit instructions.";

    [Test]
    public async Task TenantAdminAndAdviser_Get400OnType_AndNothingIsWritten()
    {
        var (tenant, _, customer, tenantAdmin) = await AccountHttp.SignInTenantAdmin();
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", tenant.Code);
        var accountId = await OpenCustodialAsync(tenantAdmin.AccessToken, customer.PublicId);

        foreach (var token in new[] { tenantAdmin.AccessToken, adviser.AccessToken })
        {
            var response = await PostAsync(token, Cash(accountId, "TransferIn", 100m));

            (await ErrorsAsync(response))["Type"].ShouldBe([Message]);
        }

        (await TransactionCountAsync(accountId)).ShouldBe(0);
    }

    [Test]
    public async Task SystemAdmin_Gets400OnType()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var system = await TenantHttp.SignInSystemAdmin();
        var body = Cash(accountId, "TransferIn", 100m);
        body["tenantId"] = tenant.PublicId;

        var response = await PostAsync(system.AccessToken, body);

        (await ErrorsAsync(response))["Type"].ShouldBe([Message]);
        (await TransactionCountAsync(accountId)).ShouldBe(0);
    }

    [Test]
    public async Task ExternalAccount_StillTakesAHandTransferIn()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId);

        (await PostAsync(tokens.AccessToken, Cash(accountId, "TransferIn", 100m).WithBookedAt()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AManualTransferInEnteredBeforeThisSlice_CanStillBeReversedByHand()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var transferIn = await InsertManualTransferInAsync(accountId, 100m);

        var response = await ReverseAsync(tokens.AccessToken, transferIn);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0m);
    }

    [Test]
    public async Task TransferOutAndCloseOut_StayAllowed()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        (await FundAsync(tokens.AccessToken, accountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(tokens.AccessToken, Cash(accountId, "TransferOut", -40m))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, Cash(accountId, "CloseOut", -60m))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
