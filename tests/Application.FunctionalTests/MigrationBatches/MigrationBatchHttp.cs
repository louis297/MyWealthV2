using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.MigrationBatches;

/// <summary>Calls and bodies of the migration batch routes (docs/features/migration-batches.md §8).</summary>
internal static class MigrationBatchHttp
{
    public const string Path = "/migration-batches";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    /// <summary>Every route that takes an id, and the list (R1).</summary>
    public static IEnumerable<(HttpMethod Method, string Path, object? Body)> Routes(Guid id)
    {
        var action = new { rowVersion = StaleRowVersion, note = "Note" };
        yield return (HttpMethod.Get, Path, null);
        yield return (HttpMethod.Get, $"{Path}/{id}", null);
        yield return (HttpMethod.Post, Path, Body([]));
        yield return (HttpMethod.Put, $"{Path}/{id}", Body([], rowVersion: StaleRowVersion));
        foreach (var name in Actions)
        {
            yield return (HttpMethod.Post, $"{Path}/{id}/{name}", action);
        }
    }

    /// <summary>The action routes this slice has so far (R17–R20).</summary>
    public static string[] Actions { get; } = [];

    public static Dictionary<string, object?> Body(
        IEnumerable<object> entries,
        object? controlTotals = null,
        string name = "Acme Wealth transfer, November 2026",
        string previousProvider = "Acme Wealth",
        string asOfDate = "2026-11-20",
        string? rowVersion = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["previousProvider"] = previousProvider,
            ["asOfDate"] = asOfDate,
            ["controlTotals"] = controlTotals ?? Totals(0),
            ["entries"] = entries.ToList()
        };
        if (rowVersion is not null)
        {
            body["rowVersion"] = rowVersion;
        }

        return body;
    }

    public static object Totals(
        int accountCount,
        IEnumerable<object>? cash = null,
        IEnumerable<object>? quantity = null,
        IEnumerable<object>? cost = null) => new
    {
        accountCount,
        cash = (cash ?? []).ToList(),
        quantity = (quantity ?? []).ToList(),
        cost = (cost ?? []).ToList()
    };

    public static object Money(string currency, decimal amount) => new { currency, amount };

    public static object Quantity(Guid instrumentId, decimal quantity) => new { instrumentId, quantity };

    public static object Entry(Guid accountId, decimal? cash, params object[] holdings) =>
        new { accountId, externalReference = "ACME-" + accountId.ToString("N")[..6], cash, holdings };

    public static object Holding(Guid instrumentId, decimal quantity, decimal cost) => new { instrumentId, quantity, cost };

    public static Task<HttpResponseMessage> CreateAsync(string accessToken, object body) =>
        TenantHttp.Send(HttpMethod.Post, Path, accessToken, body);

    public static async Task<Guid> CreateIdAsync(string accessToken, object body) =>
        await CustodialHttp.ReadIdAsync(await CreateAsync(accessToken, body));

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, $"{Path}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<JsonElement> ListAsync(string accessToken, string query = "")
    {
        var response = await TenantHttp.Send(HttpMethod.Get, Path + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<string> RowVersionAsync(string accessToken, Guid id) =>
        (await GetAsync(accessToken, id)).GetProperty("rowVersion").GetString()!;

    public static async Task<HttpResponseMessage> ReplaceAsync(string accessToken, Guid id, Dictionary<string, object?> body)
    {
        body["rowVersion"] = await RowVersionAsync(accessToken, id);
        return await TenantHttp.Send(HttpMethod.Put, $"{Path}/{id}", accessToken, body);
    }

    /// <summary>Posts an action with the batch's current <c>rowVersion</c> (R17–R20).</summary>
    public static async Task<HttpResponseMessage> ActAsync(string accessToken, Guid id, string action, string? note = null)
    {
        var rowVersion = await RowVersionAsync(accessToken, id);
        return await TenantHttp.Send(HttpMethod.Post, $"{Path}/{id}/{action}", accessToken,
            note is null ? new { rowVersion } : new { rowVersion, note });
    }

    public static IReadOnlyList<string> Codes(JsonElement item) =>
        item.GetProperty("validation").GetProperty("issues").EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString()!).ToList();

    public static IReadOnlyList<Guid> Ids(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    public static async Task<MigrationBatch> BatchAsync(Guid id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.MigrationBatches.IgnoreQueryFilters().AsNoTracking()
            .Include(batch => batch.Entries)
            .SingleAsync(batch => batch.PublicId == id);
    }

    /// <summary>Closes an empty Custodial account in the database, as the account closure flow ends.</summary>
    public static async Task CloseInDatabaseAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        account.Close();
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// One tenant with two TenantAdmins (Tess signs in, Tia is the second), an Adviser (Ann) with a Customer (Ned) who
/// has two empty Custodial USD accounts, the tenant's USD client money account and VTI quoted in USD
/// (migration-batches §10).
/// </summary>
internal sealed record MigrationWorld(
    DepositMatchingWorld Bank,
    TokenResponse SecondTenantAdmin,
    TokenResponse AdviserToken,
    TokenResponse CustomerToken,
    Guid SecondAccountId,
    Guid InstrumentId)
{
    public string TenantAdmin => Bank.TenantAdmin.AccessToken;

    public string Tia => SecondTenantAdmin.AccessToken;

    public Guid AccountId => Bank.AccountId;

    public Tenant Tenant => Bank.Tenant;

    public static async Task<MigrationWorld> CreateAsync(string name = "North Advisory", string code = "north-advisory")
    {
        var bank = await DepositMatchingWorld.CreateAsync(name, code);
        var tia = await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tia Admin", $"tia@{code}.example", "Password1!", bank.Tenant.Id);
        var second = await CustodialHttp.OpenCustodialAsync(bank.TenantAdmin.AccessToken, bank.Customer.PublicId, "Second portfolio");
        var instrument = await CustodialHttp.CreateInstrumentAsync(bank.TenantAdmin.AccessToken, "VTI", "USD", 220m);
        return new MigrationWorld(
            bank,
            await TestApp.IssueAccessTokenAsync(tia),
            await TestApp.IssueAccessTokenAsync(bank.Adviser),
            await TestApp.IssueAccessTokenAsync(bank.Customer),
            second,
            instrument);
    }

    /// <summary>The body of R8.1: two entries with cash and VTI, and control totals that agree.</summary>
    public Dictionary<string, object?> ValidBody() => MigrationBatchHttp.Body(
        [
            MigrationBatchHttp.Entry(AccountId, 150000m, MigrationBatchHttp.Holding(InstrumentId, 800m, 176000m)),
            MigrationBatchHttp.Entry(SecondAccountId, 100000m, MigrationBatchHttp.Holding(InstrumentId, 400m, 88000m))
        ],
        MigrationBatchHttp.Totals(2,
            [MigrationBatchHttp.Money("USD", 250000m)],
            [MigrationBatchHttp.Quantity(InstrumentId, 1200m)],
            [MigrationBatchHttp.Money("USD", 264000m)]));
}
