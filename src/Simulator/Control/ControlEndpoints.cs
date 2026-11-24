using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Bank;
using MyWealthV2.Simulator.Broker;
using MyWealthV2.Simulator.Custodian;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Control;

/// <summary>Simulator spec §8: the control surface. Every write is a POST with a JSON body (R19).</summary>
public static class ControlEndpoints
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    internal static readonly JsonSerializerOptions DetailJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static WebApplication MapControlEndpoints(this WebApplication app)
    {
        var control = app.MapGroup(SimulatorAuthentication.ControlPrefix)
            .RequireAuthorization(SimulatorAuthentication.OperatorPolicy)
            .WithTags("Control");

        control.MapGet("/world", GetWorldAsync);
        control.MapPost("/clock/advance", AdvanceAsync);
        control.MapPost("/clock/pause", (EmptyRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time) =>
            WriteAsync(http, db, time, ControlActions.ClockPaused, body, (world, now) => world.Pause(now)));
        control.MapPost("/clock/resume", (EmptyRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time) =>
            WriteAsync(http, db, time, ControlActions.ClockResumed, body, (world, now) => world.Resume(now)));
        control.MapPost("/party/outage", OutageAsync);
        control.MapPost("/reset", ResetAsync);
        control.MapGet("/log", GetLogAsync);
        control.MapBankControlEndpoints();
        control.MapMarketControlEndpoints();
        control.MapBrokerControlEndpoints();
        control.MapCustodianControlEndpoints();

        return app;
    }

    private static async Task<IResult> GetWorldAsync(SimulatorDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        return Results.Ok(WorldItem.From(world, time.GetUtcNow()));
    }

    private static Task<IResult> AdvanceAsync(
        AdvanceRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var hasBy = !string.IsNullOrWhiteSpace(body.By);
        if (hasBy == body.To.HasValue)
        {
            return Task.FromResult(Refused("By", "Give exactly one of by or to."));
        }

        TimeSpan? by = null;
        if (hasBy)
        {
            try
            {
                by = XmlConvert.ToTimeSpan(body.By!);
            }
            catch (FormatException)
            {
                return Task.FromResult(Refused("By", "by must be an ISO 8601 duration, for example P2D or PT6H."));
            }
        }

        return WriteAsync(http, db, time, ControlActions.ClockAdvanced, body, (world, now) =>
        {
            if (by is { } duration)
            {
                world.Advance(duration, now);
            }
            else
            {
                world.AdvanceTo(body.To!.Value, now);
            }
        });
    }

    private static Task<IResult> OutageAsync(
        OutageRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (body.On is not { } on)
        {
            return Task.FromResult(Refused("On", "on is required."));
        }

        return WriteAsync(http, db, time, ControlActions.PartyOutageChanged, body, (world, _) => world.SetOutage(on));
    }

    private static async Task<IResult> ResetAsync(
        ResetRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time,
        BankStandingAccounts standing,
        MarketStandingInstruments standingInstruments,
        CustodianStandingAccounts standingCustody)
    {
        var seed = body.Seed ?? SeededRandom.NewSeed();

        // R24: every party table is emptied in the same transaction; deposit-matching R47 opens the standing
        // accounts of the new world again.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);
            await db.BankPayments.ExecuteDeleteAsync(http.RequestAborted);
            await db.BankLines.ExecuteUpdateAsync(
                setters => setters.SetProperty(line => line.ReturnOfLineId, (long?)null), http.RequestAborted);
            await db.BankFeedPlaces.ExecuteDeleteAsync(http.RequestAborted);
            await db.BankLines.ExecuteDeleteAsync(http.RequestAborted);
            await db.BankAccounts.ExecuteDeleteAsync(http.RequestAborted);
            await standing.OpenMissingAsync(db, time.GetUtcNow(), http.RequestAborted);

            // Orders R65: the broker's orders, fills and reports are emptied and its settings set back.
            await db.BrokerReports.ExecuteDeleteAsync(http.RequestAborted);
            await db.BrokerFills.ExecuteDeleteAsync(http.RequestAborted);
            await db.BrokerOrders.ExecuteDeleteAsync(http.RequestAborted);
            await db.BrokerSettlements.ExecuteDeleteAsync(http.RequestAborted);
            await db.BrokerSettings.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(settings => settings.Mode, BrokerOrderMode.Fill)
                    .SetProperty(settings => settings.FillPercent, BrokerRules.DefaultFillPercent)
                    .SetProperty(settings => settings.RejectReason, (BrokerRejectReason?)null)
                    .SetProperty(settings => settings.SlippagePercent, BrokerRules.DefaultSlippagePercent)
                    .SetProperty(settings => settings.SettlementDays, BrokerRules.DefaultSettlementDays)
                    .SetProperty(settings => settings.SettlementMode, BrokerSettlementMode.Pay),
                http.RequestAborted);

            // Simulated instruments R23, corporate-actions R47: the market and its corporate actions are emptied and its
            // standing instruments added again, priced on the first day of the new world (its clock starts at real now).
            // Dividends R63: the custodian's dividend payments point at the market's dividends and its accounts.
            await db.CustodianDividendPayments.ExecuteDeleteAsync(http.RequestAborted);
            await db.MarketCorporateActionVersions.ExecuteDeleteAsync(http.RequestAborted);
            await db.MarketCorporateActions.ExecuteDeleteAsync(http.RequestAborted);
            await db.MarketPrices.ExecuteDeleteAsync(http.RequestAborted);
            await db.MarketInstruments.ExecuteDeleteAsync(http.RequestAborted);
            await standingInstruments.AddMissingAsync(db, MarketPricePath.Day(time.GetUtcNow()), http.RequestAborted);
            // In-kind-transfers R71: the custodian's books are emptied, its settings set back, and its standing accounts
            // opened again with their positions.
            await db.CustodianTransfers.ExecuteDeleteAsync(http.RequestAborted);
            await db.CustodianMovements.ExecuteDeleteAsync(http.RequestAborted);
            await db.CustodianAccounts.ExecuteDeleteAsync(http.RequestAborted);
            await db.CustodianSettings.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(settings => settings.TransferMode, CustodianTransferMode.Execute)
                    .SetProperty(settings => settings.RejectReason, (string?)null)
                    .SetProperty(settings => settings.RoundingMode, CustodianRoundingMode.Fractional),
                http.RequestAborted);
            await standingCustody.OpenMissingAsync(db, time.GetUtcNow(), http.RequestAborted);
            var result = await WriteAsync(http, db, time, ControlActions.WorldReset, body, (world, now) =>
                world.Reset(Guid.NewGuid(), seed, now));
            if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK })
            {
                await transaction.CommitAsync(http.RequestAborted);
            }

            return result;
        });
    }

    private static async Task<IResult> GetLogAsync(
        SimulatorDbContext db,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var total = await db.ControlLog.CountAsync(cancellationToken);
        var rows = await db.ControlLog.AsNoTracking()
            .OrderByDescending(entry => entry.At)
            .ThenByDescending(entry => entry.Id)
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        var items = rows.Select(entry => new ControlLogItem(
                entry.At,
                entry.SimulatorAt,
                entry.Actor,
                entry.Action,
                JsonDocument.Parse(entry.Detail).RootElement.Clone()))
            .ToList();
        return Results.Ok(new ControlLogPage(items, number, size, total));
    }

    /// <summary>
    /// One control write: apply it to the world, append the log row and save both in one transaction.
    /// A refused write (400) appends nothing; a concurrent write on the world row (R29) returns 409.
    /// </summary>
    internal static async Task<IResult> WriteAsync(
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time,
        string action,
        object body,
        Action<World, DateTimeOffset> apply)
    {
        var world = await db.Worlds.SingleAsync(http.RequestAborted);
        var realNow = time.GetUtcNow();
        try
        {
            apply(world, realNow);
        }
        catch (WorldRuleException refused)
        {
            return Refused(refused.Field, refused.Message);
        }

        db.ControlLog.Add(ControlLogEntry.Create(
            realNow,
            world.Now(realNow),
            Actor(http.User),
            action,
            JsonSerializer.Serialize(body, body.GetType(), DetailJson)));
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The world was changed by another write. Read it again and retry.");
        }

        return Results.Ok(WorldItem.From(world, realNow));
    }

    internal static IResult Refused(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    internal static string Actor(ClaimsPrincipal user) =>
        user.FindFirst("email")?.Value ?? user.Identity?.Name ?? "unknown";
}

public sealed record AdvanceRequest(string? By, DateTimeOffset? To);

public sealed record OutageRequest(bool? On);

public sealed record ResetRequest(int? Seed);

/// <summary>Pause and resume take an empty JSON object.</summary>
public sealed record EmptyRequest;

public sealed record WorldItem(
    Guid WorldId,
    int Seed,
    DateTimeOffset Now,
    bool Paused,
    bool PartyOutage,
    DateTimeOffset StartedAt)
{
    public static WorldItem From(World world, DateTimeOffset realNow) => new(
        world.WorldId,
        world.Seed,
        world.Now(realNow),
        world.Paused,
        world.PartyOutage,
        world.StartedAt);
}

public sealed record ControlLogItem(
    DateTimeOffset At,
    DateTimeOffset SimulatorAt,
    string Actor,
    string Action,
    JsonElement Detail);

public sealed record ControlLogPage(IReadOnlyList<ControlLogItem> Items, int Page, int PageSize, int TotalCount);
