using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>Safekeeping account routes (docs/features/in-kind-transfers.md R2–R7).</summary>
internal static class SafekeepingHttp
{
    /// <summary>The custodian connection the functional host's fake <c>ICustodianConnections</c> knows.</summary>
    public const string Connection = "simulator";

    public static string Path(Guid tenantId) => $"/tenants/{tenantId}/safekeeping-accounts";

    public static object Body(
        string currency = "USD",
        string? accountNumber = null,
        string custodianName = "Simulator Custody",
        string accountName = "North Advisory Nominees Ltd",
        string? custodianConnection = Connection) => new
        {
            currency,
            custodianName,
            accountName,
            accountNumber = accountNumber ?? UniqueNumber(),
            custodianConnection
        };

    public static string UniqueNumber() => "SK-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    public static async Task<Guid> CreateAsync(string accessToken, Guid tenantId, object body)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, Path(tenantId), accessToken, body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> GetAsync(string accessToken, Guid tenantId, Guid id)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, $"{Path(tenantId)}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<string> RowVersionAsync(string accessToken, Guid tenantId, Guid id) =>
        (await GetAsync(accessToken, tenantId, id)).GetProperty("rowVersion").GetString()!;

    public static async Task<HttpResponseMessage> SetActiveAsync(string accessToken, Guid tenantId, Guid id, bool active)
    {
        var rowVersion = await RowVersionAsync(accessToken, tenantId, id);
        return await TenantHttp.Send(
            HttpMethod.Post, $"{Path(tenantId)}/{id}/{(active ? "enable" : "disable")}", accessToken, new { rowVersion });
    }

    public static async Task<int> CountAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SafekeepingAccounts.IgnoreQueryFilters().CountAsync(row => row.TenantId == tenantId);
    }

    /// <summary>
    /// Writes an active safekeeping account of <paramref name="tenantId"/> in <paramref name="currency"/> straight to
    /// the database, unless one is active. Tests that request a transfer or place an order with one need it (R6).
    /// </summary>
    public static async Task<SafekeepingAccount> EnsureActiveAsync(int tenantId, string currency, string? accountNumber = null)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var code = currency.Trim().ToUpperInvariant();
        var existing = await db.SafekeepingAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.Currency == code && row.IsActive);
        if (existing is not null)
        {
            return existing;
        }

        var catalog = await db.Set<Currency>().SingleAsync(row => row.Code == code);
        var row = SafekeepingAccount.Create(tenantId, catalog, "Simulator Custody", "Nominees", accountNumber ?? UniqueNumber(), Connection);
        row.CreatedBy = "test";
        db.SafekeepingAccounts.Add(row);
        await db.SaveChangesAsync();
        return row;
    }
}
