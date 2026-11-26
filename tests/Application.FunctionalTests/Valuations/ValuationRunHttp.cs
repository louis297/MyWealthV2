using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>The <c>/valuation-runs</c> routes and the stored valuation reads (docs/features/valuation-run.md §8).</summary>
internal static class ValuationRunHttp
{
    public const string Runs = "/valuation-runs";

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Task<HttpResponseMessage> StartAsync(string accessToken, Guid tenantId, DateOnly throughDate) =>
        Send(HttpMethod.Post, Runs, accessToken, new { tenantId, throughDate = throughDate.ToString("yyyy-MM-dd") });

    /// <summary>Starts a run through <paramref name="throughDate"/> as a SystemAdmin; asserts 201 and the Location.</summary>
    public static async Task<Guid> StartRunAsync(Guid tenantId, DateOnly throughDate)
    {
        var system = await AccountHttp.SignInSystemAdmin();
        var response = await StartAsync(system.AccessToken, tenantId, throughDate);
        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldEndWith($"{Runs}/{id}");
        return id;
    }

    public static async Task<JsonElement> GetAsync(string accessToken, string path)
    {
        var response = await Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static List<Guid> Ids(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    public static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Tenants] SET [IsActive] = 0 WHERE [Id] = {tenantId}");
    }
}
