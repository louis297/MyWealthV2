using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Valuations;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary><c>GET /net-worth/funds-under-management</c> (docs/features/valuation-history.md §8).</summary>
internal static class FundsHttp
{
    public const string Route = "/net-worth/funds-under-management";

    public static Task<HttpResponseMessage> Send(string? accessToken, string query = "") =>
        AccountHttp.Send(HttpMethod.Get, Route + query, accessToken);

    public static Task<JsonElement> GetAsync(string accessToken, string query = "") =>
        ValuationRunHttp.GetAsync(accessToken, Route + query);

    public static string Range(DateOnly from, DateOnly to) => $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

    public static JsonElement Day(JsonElement series, DateOnly date) =>
        series.GetProperty("days").EnumerateArray().Single(day => day.GetProperty("date").GetString() == date.ToString("yyyy-MM-dd"));

    public static List<string> Dates(JsonElement series) =>
        series.GetProperty("days").EnumerateArray().Select(day => day.GetProperty("date").GetString()!).ToList();
}
