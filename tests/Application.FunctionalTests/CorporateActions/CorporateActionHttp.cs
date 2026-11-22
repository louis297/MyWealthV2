using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>The <c>/corporate-actions</c> routes (docs/features/corporate-actions.md §8) and the state the corporate
/// action tests set up.</summary>
internal static class CorporateActionHttp
{
    public const string Root = "/corporate-actions";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Dictionary<string, object?> CreateBody(
        Guid instrumentId,
        string kind = "Split",
        int ratioNew = 4,
        int ratioHeld = 1,
        DateOnly? exDate = null,
        string? memo = "Announced by the issuer") => new()
    {
        ["instrumentId"] = instrumentId,
        ["kind"] = kind,
        ["ratioNew"] = ratioNew,
        ["ratioHeld"] = ratioHeld,
        ["exDate"] = (exDate ?? Today.AddDays(9)).ToString("yyyy-MM-dd"),
        ["recordDate"] = null,
        ["paymentDate"] = null,
        ["memo"] = memo
    };

    public static Dictionary<string, object?> UpdateBody(
        string? rowVersion,
        string kind = "Split",
        int ratioNew = 3,
        int ratioHeld = 1,
        DateOnly? exDate = null,
        DateOnly? recordDate = null,
        DateOnly? paymentDate = null,
        string? memo = null) => new()
    {
        ["kind"] = kind,
        ["ratioNew"] = ratioNew,
        ["ratioHeld"] = ratioHeld,
        ["exDate"] = (exDate ?? Today.AddDays(10)).ToString("yyyy-MM-dd"),
        ["recordDate"] = recordDate?.ToString("yyyy-MM-dd"),
        ["paymentDate"] = paymentDate?.ToString("yyyy-MM-dd"),
        ["memo"] = memo,
        ["rowVersion"] = rowVersion
    };

    public static async Task<Guid> CreateAsync(string accessToken, Dictionary<string, object?> body)
    {
        var response = await Send(HttpMethod.Post, Root, accessToken, body);
        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldEndWith($"{Root}/{id}");
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

    public static Task<JsonElement> ItemAsync(string accessToken, Guid id) => GetAsync(accessToken, $"{Root}/{id}");

    public static async Task<string> RowVersionAsync(string accessToken, Guid id) =>
        (await ItemAsync(accessToken, id)).GetProperty("rowVersion").GetString()!;

    public static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    /// <summary>Every route of §8 under <c>/corporate-actions</c> for the action <paramref name="id"/>.</summary>
    public static IEnumerable<(HttpMethod Method, string Path, object? Body)> Routes(Guid id, Guid instrumentId)
    {
        yield return (HttpMethod.Get, Root, null);
        yield return (HttpMethod.Get, $"{Root}/{id}", null);
        yield return (HttpMethod.Post, Root, CreateBody(instrumentId, exDate: Today.AddDays(30)));
        yield return (HttpMethod.Put, $"{Root}/{id}", UpdateBody(StaleRowVersion));
        yield return (HttpMethod.Post, $"{Root}/{id}/cancel", new { rowVersion = StaleRowVersion, note = "No" });
    }

    /// <summary>A <c>Feed</c> corporate action on <paramref name="instrumentId"/>, written straight to the database as
    /// the feed processing would.</summary>
    public static async Task<Guid> AddFeedActionAsync(Guid instrumentId, string partyActionId = "w-CA1", DateOnly? exDate = null)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentId);
        var action = CorporateAction.Announce(
            instrument.TenantId, instrument.Id, "simulator", partyActionId, 1, CorporateActionKind.Split,
            new CorporateActionRatio(2, 1), exDate ?? Today.AddDays(3), null, null);
        action.CreatedBy = "system";
        db.CorporateActions.Add(action);
        await db.SaveChangesAsync();
        return action.PublicId;
    }

    /// <summary>Moves a corporate action straight to <c>Applied</c> in the database, with no entitlement.</summary>
    public static async Task MarkAppliedAsync(Guid actionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var action = await db.CorporateActions.IgnoreQueryFilters().SingleAsync(row => row.PublicId == actionId);
        action.MakeEffective(DateTimeOffset.UtcNow);
        action.MarkApplied(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Stores a price row of <paramref name="instrumentId"/> dated <paramref name="asOf"/>.</summary>
    public static async Task AddPriceAsync(Guid instrumentId, DateOnly asOf, decimal price)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentId);
        db.InstrumentPrices.Add(new InstrumentPrice
        {
            InstrumentId = instrument.Id,
            AsOfDate = asOf,
            Price = price,
            Source = "EODHD",
            FetchedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
