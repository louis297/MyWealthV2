using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Control;

/// <summary>Simulator spec §8: the control surface. Every write is a POST with a JSON body (R19).</summary>
public static class ControlEndpoints
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly JsonSerializerOptions DetailJson = new(JsonSerializerDefaults.Web)
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

    private static Task<IResult> ResetAsync(
        ResetRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var seed = body.Seed ?? SeededRandom.NewSeed();

        // R24: every party table is emptied here, in the same transaction. There is none in this slice.
        return WriteAsync(http, db, time, ControlActions.WorldReset, body, (world, now) =>
            world.Reset(Guid.NewGuid(), seed, now));
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
    private static async Task<IResult> WriteAsync(
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

    private static IResult Refused(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string Actor(ClaimsPrincipal user) =>
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
