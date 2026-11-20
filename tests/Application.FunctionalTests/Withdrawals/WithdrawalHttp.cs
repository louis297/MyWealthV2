using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Withdrawals;

/// <summary>The <c>/withdrawals</c> routes (docs/features/withdrawals.md §8).</summary>
internal static class WithdrawalHttp
{
    public const string Path = "/withdrawals";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Dictionary<string, object?> Body(Guid accountId, Guid nominatedBankAccountId, decimal amount) => new()
    {
        ["accountId"] = accountId,
        ["nominatedBankAccountId"] = nominatedBankAccountId,
        ["amount"] = amount
    };

    public static Task<HttpResponseMessage> PostAsync(string accessToken, Dictionary<string, object?> body, Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, Path, accessToken, body, key ?? Guid.NewGuid());

    public static async Task<Guid> RequestAsync(string accessToken, Guid accountId, Guid nominatedBankAccountId, decimal amount)
    {
        var response = await PostAsync(accessToken, Body(accountId, nominatedBankAccountId, amount));
        return await CustodialHttp.ReadIdAsync(response);
    }

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"{Path}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<List<Guid>> ListIdsAsync(string accessToken, string query = "")
    {
        var response = await Send(HttpMethod.Get, Path + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    /// <summary>Posts an action with the withdrawal's current <c>rowVersion</c>, read as a TenantAdmin of its tenant.</summary>
    public static async Task<HttpResponseMessage> ActAsync(string accessToken, Guid id, string action, string? note = null, string? readToken = null)
    {
        var rowVersion = (await GetAsync(readToken ?? accessToken, id)).GetProperty("rowVersion").GetString();
        return await Send(HttpMethod.Post, $"{Path}/{id}/{action}", accessToken, new { note, rowVersion });
    }

    /// <summary>Every route, with a body each action accepts.</summary>
    public static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(Guid id)
    {
        yield return (HttpMethod.Get, Path, null);
        yield return (HttpMethod.Get, $"{Path}/{id}", null);
        yield return (HttpMethod.Post, Path, Body(Guid.NewGuid(), Guid.NewGuid(), 1m));
        yield return (HttpMethod.Post, $"{Path}/{id}/approve", new { rowVersion = StaleRowVersion });
        yield return (HttpMethod.Post, $"{Path}/{id}/reject", new { note = "No", rowVersion = StaleRowVersion });
        yield return (HttpMethod.Post, $"{Path}/{id}/cancel", new { rowVersion = StaleRowVersion });
    }
}

/// <summary>
/// The withdrawals test world (withdrawals §10): a tenant with two TenantAdmins (Tess signs in, Tia), an Adviser
/// (Ann) with a Customer (Ned), a second Adviser (Bob) with a Customer (Olga). Ned has an Open Custodial USD account
/// funded with 1 000.00 through the fake bank feed and a Verified USD nominated bank account, added by Ann and
/// verified by Tess.
/// </summary>
internal sealed record WithdrawalWorld(
    NominatedWorld People,
    Guid AccountId,
    string DepositReference,
    Guid NominatedBankAccountId,
    ClientMoneyAccount ClientMoney)
{
    public Tenant Tenant => People.Tenant;
    public User Customer => People.Customer;
    public string TenantAdmin => People.TenantAdmin.AccessToken;
    public string SecondTenantAdmin => People.SecondTenantAdmin.AccessToken;
    public string Adviser => People.AdviserToken.AccessToken;
    public string CustomerToken => People.CustomerToken.AccessToken;

    public static async Task<WithdrawalWorld> CreateAsync(
        string name = "North Advisory",
        string code = "north-advisory",
        decimal funding = 1000.00m)
    {
        var people = await NominatedWorld.CreateAsync(name, code);
        var accountId = await CustodialHttp.OpenCustodialAsync(people.TenantAdmin.AccessToken, people.Customer.PublicId);
        if (funding > 0)
        {
            (await CustodialHttp.FundAsync(people.TenantAdmin.AccessToken, accountId, funding)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var nominated = await NominatedBankAccountHttp.AddAsync(people.AdviserToken.AccessToken, people.Customer.PublicId);
        await NominatedBankAccountHttp.VerifyAsync(people.TenantAdmin.AccessToken, nominated);
        return new WithdrawalWorld(
            people,
            accountId,
            await BankFeedHttp.DepositReferenceAsync(accountId),
            nominated,
            await BankFeedHttp.ClientMoneyAccountAsync(people.Tenant.Id, "USD"));
    }

    public async Task<(decimal Cash, decimal Reserved, decimal Available)> CashAsync(Guid? accountId = null)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId ?? AccountId}/cash-balance", TenantAdmin);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return (root.GetProperty("cashBalance").GetDecimal(), root.GetProperty("reservedCash").GetDecimal(),
            root.GetProperty("availableCash").GetDecimal());
    }

    /// <summary>Requests <paramref name="amount"/> as the Adviser.</summary>
    public Task<Guid> RequestAsync(decimal amount) => WithdrawalHttp.RequestAsync(Adviser, AccountId, NominatedBankAccountId, amount);

    /// <summary>Requests as the Adviser and approves as Tess.</summary>
    public async Task<Guid> ApprovedAsync(decimal amount)
    {
        var id = await RequestAsync(amount);
        var response = await WithdrawalHttp.ActAsync(TenantAdmin, id, "approve");
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return id;
    }
}
