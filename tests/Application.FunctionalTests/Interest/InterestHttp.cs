using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>The <c>/interest</c> routes (docs/features/interest-run.md §8) and the state the interest tests set up.</summary>
internal static class InterestHttp
{
    public const string Schedules = "/interest/rate-schedules";
    public const string Runs = "/interest/runs";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static object Band(decimal from, decimal ratePercent) => new { from, ratePercent };

    /// <summary>A create body: 3 % a year, from <paramref name="effectiveFrom"/>.</summary>
    public static Dictionary<string, object?> ScheduleBody(string currency, DateOnly effectiveFrom, params object[] bands) => new()
    {
        ["currency"] = currency,
        ["effectiveFrom"] = effectiveFrom.ToString("yyyy-MM-dd"),
        ["bands"] = bands.Length == 0 ? new[] { Band(0m, 3m) } : bands
    };

    public static Dictionary<string, object?> UpdateBody(DateOnly effectiveFrom, string? rowVersion, params object[] bands) => new()
    {
        ["effectiveFrom"] = effectiveFrom.ToString("yyyy-MM-dd"),
        ["bands"] = bands.Length == 0 ? new[] { Band(0m, 3m) } : bands,
        ["rowVersion"] = rowVersion
    };

    public static async Task<Guid> CreateScheduleAsync(string accessToken, string currency, DateOnly effectiveFrom, params object[] bands)
    {
        var response = await Send(HttpMethod.Post, Schedules, accessToken, ScheduleBody(currency, effectiveFrom, bands));
        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldEndWith($"{Schedules}/{id}");
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

    public static async Task<string> RowVersionAsync(string accessToken, Guid scheduleId) =>
        (await GetAsync(accessToken, $"{Schedules}/{scheduleId}")).GetProperty("rowVersion").GetString()!;

    /// <summary>A Custodial USD account of <paramref name="customerId"/>, with the tenant's client money account.</summary>
    public static async Task<Guid> OpenCustodialAsync(int tenantId, string accessToken, Guid customerId, string currency = "USD")
    {
        await BankFeedHttp.ClientMoneyAccountAsync(tenantId, currency);
        return await CustodialHttp.OpenCustodialAsync(accessToken, customerId, currency: currency);
    }

    /// <summary>Sets <c>Accounts.InterestAccruedThrough</c> straight in the database, as if the account had been accrued.</summary>
    public static async Task SetAccruedThroughAsync(Guid accountId, DateOnly? day)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Accounts] SET [InterestAccruedThrough] = {day?.ToDateTime(TimeOnly.MinValue)} WHERE [PublicId] = {accountId}");
    }

    public static async Task<int> CountSchedulesAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [InterestRateSchedules]").SingleAsync();
    }
}
