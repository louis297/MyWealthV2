using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>Withdrawals R35: <c>reservedCash</c> and <c>availableCash</c> on the account reads.</summary>
[Parallelizable]
public class AccountAvailableCashTests : TestBase
{
    [Test]
    public async Task OpenWithdrawalsReserveCash_OnGetListAndCashBalance_AndClosedOnesDoNot()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var other = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Other");
        (await CustodialHttp.FundAsync(tokens.AccessToken, accountId, 1000.00m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CustodialHttp.FundAsync(tokens.AccessToken, other, 500.00m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await WithdrawalDb.InsertAsync(accountId, 100.00m, WithdrawalStatus.Requested);
        await WithdrawalDb.InsertAsync(accountId, 50.00m, WithdrawalStatus.Approved);
        await WithdrawalDb.InsertAsync(accountId, 25.00m, WithdrawalStatus.Sent);
        foreach (var status in new[] { WithdrawalStatus.Paid, WithdrawalStatus.Rejected, WithdrawalStatus.Cancelled, WithdrawalStatus.Failed, WithdrawalStatus.Returned })
        {
            await WithdrawalDb.InsertAsync(accountId, 7.00m, status);
        }

        await WithdrawalDb.InsertAsync(other, 400.00m, WithdrawalStatus.Requested);

        var item = await ReadAsync(tokens.AccessToken, $"/accounts/{accountId}");
        ShouldShow(item, 1000.00m, 175.00m, 825.00m);
        var listed = (await ReadAsync(tokens.AccessToken, $"/accounts?customerId={customer.PublicId}")).GetProperty("items")
            .EnumerateArray().ToDictionary(row => row.GetProperty("id").GetGuid());
        ShouldShow(listed[accountId], 1000.00m, 175.00m, 825.00m);
        ShouldShow(listed[other], 500.00m, 400.00m, 100.00m);
        var cash = await ReadAsync(tokens.AccessToken, $"/accounts/{accountId}/cash-balance");
        ShouldShow(cash, 1000.00m, 175.00m, 825.00m);
    }

    [Test]
    public async Task AnAccountWithoutWithdrawals_HasNothingReserved_AndAnExternalAccountToo()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var custodial = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var external = await CustodialHttp.OpenExternalAsync(tokens.AccessToken, customer.PublicId);
        (await CustodialHttp.FundAsync(tokens.AccessToken, custodial, 80.00m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CustodialHttp.FundAsync(tokens.AccessToken, external, 60.00m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        ShouldShow(await ReadAsync(tokens.AccessToken, $"/accounts/{custodial}"), 80.00m, 0m, 80.00m);
        ShouldShow(await ReadAsync(tokens.AccessToken, $"/accounts/{external}"), 60.00m, 0m, 60.00m);
        ShouldShow(await ReadAsync(tokens.AccessToken, $"/accounts/{external}/cash-balance"), 60.00m, 0m, 60.00m);
    }

    private static void ShouldShow(JsonElement item, decimal cash, decimal reserved, decimal available)
    {
        item.GetProperty("cashBalance").GetDecimal().ShouldBe(cash);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(reserved);
        item.GetProperty("availableCash").GetDecimal().ShouldBe(available);
    }

    private static async Task<JsonElement> ReadAsync(string accessToken, string path)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
