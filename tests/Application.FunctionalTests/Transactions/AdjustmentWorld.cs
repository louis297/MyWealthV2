using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>
/// One tenant for the adjustments tests (docs/features/adjustments.md): a TenantAdmin, an Adviser and their Customer,
/// a Custodial USD brokerage, an External USD brokerage and one USD instrument priced at 25.
/// </summary>
internal sealed record AdjustmentWorld(
    Tenant Tenant,
    User Adviser,
    User Customer,
    string Admin,
    string AdviserToken,
    string CustomerToken,
    Guid AccountId,
    Guid ExternalAccountId,
    Guid InstrumentId)
{
    public const string Path = "/transactions/adjustment";

    public static async Task<AdjustmentWorld> CreateAsync(string code = "adjust-firm")
    {
        var adviserEmail = $"ann@{code}.example";
        var customerEmail = $"ned@{code}.example";
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin(
            $"Firm {code}", code, $"tess@{code}.example", "Password1!", adviserEmail, customerEmail);
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(adviserEmail, "Password1!", code);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Managed", "USD");
        var externalId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, "Held elsewhere");
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD", 25m);
        return new AdjustmentWorld(
            tenant,
            adviser,
            customer,
            tokens.AccessToken,
            adviserTokens.AccessToken,
            customerTokens.AccessToken,
            accountId,
            externalId,
            instrumentId);
    }

    /// <summary>An adjustment body with reason <c>EntryError</c> and a memo; add <c>amount</c> or <c>security</c>.</summary>
    public static Dictionary<string, object?> Body(
        Guid accountId,
        decimal? amount = null,
        object? security = null,
        string reason = "EntryError",
        string memo = "Correction after the broker statement")
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["reason"] = reason,
            ["memo"] = memo
        };
        if (amount is not null)
        {
            body["amount"] = amount;
        }

        if (security is not null)
        {
            body["security"] = security;
        }

        return body;
    }

    public static object Holding(Guid instrumentId, decimal quantityChange, decimal costChange) =>
        new { instrumentId, quantityChange, costChange };

    public static Task<HttpResponseMessage> PostAsync(
        string accessToken,
        Dictionary<string, object?> body,
        Guid? idempotencyKey = null) =>
        Custody.CustodialHttp.PostAsync(accessToken, body, Path, idempotencyKey);

    public Task<HttpResponseMessage> AdjustAsync(Dictionary<string, object?> body, Guid? idempotencyKey = null) =>
        PostAsync(Admin, body, idempotencyKey);

    public async Task FundAsync(decimal amount) =>
        (await Custody.CustodialHttp.FundAsync(Admin, AccountId, amount)).StatusCode.ShouldBe(HttpStatusCode.Created);

    /// <summary>A <c>Buy</c> at 25.00 typed before orders (orders R48): source <c>Manual</c>, so it can be reversed by hand.</summary>
    public Task<Guid> BuyAsync(decimal quantity) =>
        Custody.CustodialHttp.InsertManualTradeAsync(AccountId, "Buy", InstrumentId, quantity);

    /// <summary>The holding's quantity and cost, or null when the Account holds none of the instrument.</summary>
    public async Task<(decimal Quantity, decimal Cost)?> HoldingAsync(Guid? accountId = null, Guid? instrumentId = null)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId ?? AccountId}/holdings", Admin);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Where(row => row.GetProperty("instrumentId").GetGuid() == (instrumentId ?? InstrumentId))
            .Select(row => (row.GetProperty("quantity").GetDecimal(), row.GetProperty("cost").GetDecimal()))
            .ToList();
        return item.Count == 0 ? null : item.Single();
    }

    public Task<decimal> CashAsync(Guid? accountId = null) => CashBalanceAsync(Admin, accountId ?? AccountId);

    public static async Task DisableInstrumentAsync(Guid instrumentId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == instrumentId);
        instrument.Disable();
        await db.SaveChangesAsync();
    }
}
