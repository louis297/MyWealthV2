using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;

/// <summary>Client money account routes (docs/features/client-money-accounts.md §8).</summary>
internal static class ClientMoneyHttp
{
    public static string Path(Guid tenantId) => $"/tenants/{tenantId}/client-money-accounts";

    public static object Body(
        string currency = "USD",
        string? accountNumber = null,
        string bankName = "Simulator Bank",
        string accountName = "North Advisory Ltd Client Funds Trust Account") => new
        {
            currency,
            bankName,
            accountName,
            accountNumber = accountNumber ?? UniqueNumber()
        };

    public static string UniqueNumber() => "N-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

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

    /// <summary>Disables or enables the row with its current <c>rowVersion</c>.</summary>
    public static async Task<HttpResponseMessage> SetActiveAsync(string accessToken, Guid tenantId, Guid id, bool active)
    {
        var rowVersion = await RowVersionAsync(accessToken, tenantId, id);
        return await TenantHttp.Send(
            HttpMethod.Post,
            $"{Path(tenantId)}/{id}/{(active ? "enable" : "disable")}",
            accessToken,
            new { rowVersion });
    }

    /// <summary>
    /// Ensures the tenant has an active client money account in <paramref name="currency"/>, written straight to
    /// the database. Tests that open a Custodial account need one (client-money-accounts R11).
    /// </summary>
    public static async Task EnsureActiveAsync(int tenantId, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var code = currency.Trim().ToUpperInvariant();
        if (await db.ClientMoneyAccounts.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == tenantId && row.Currency == code && row.IsActive))
        {
            return;
        }

        var catalogCurrency = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == code);
        db.ClientMoneyAccounts.Add(ClientMoneyAccount.Create(
            tenantId, catalogCurrency, "Simulator Bank", "Test Client Funds Trust Account", $"T{tenantId}-{code}"));
        await db.SaveChangesAsync();
    }

    /// <summary>Ensures a client money account for the tenant of <paramref name="customerPublicId"/>.</summary>
    public static async Task EnsureActiveForCustomerAsync(Guid customerPublicId, string currency)
    {
        int tenantId;
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            tenantId = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.PublicId == customerPublicId)
                .Select(row => row.TenantId ?? 0)
                .SingleAsync();
        }

        await EnsureActiveAsync(tenantId, currency);
    }

    public static async Task<int> CountAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ClientMoneyAccounts.IgnoreQueryFilters().CountAsync(row => row.TenantId == tenantId);
    }
}
