using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Market;

/// <summary>Corporate-actions R39–R41, R46: the simulator market announces, amends and cancels corporate actions of
/// its instruments on the control surface. Every route brings the market up to date first (simulated-instruments R18),
/// so an action whose ex-date has come is already effective.</summary>
public static class MarketCorporateActionEndpoints
{
    public static RouteGroupBuilder MapMarketCorporateActionEndpoints(this RouteGroupBuilder market)
    {
        market.MapGet("/corporate-actions", ListAsync);
        market.MapGet("/corporate-actions/{id}", GetAsync);
        market.MapPost("/corporate-actions", AnnounceAsync);
        market.MapPost("/corporate-actions/{id}/amend", AmendAsync);
        market.MapPost("/corporate-actions/{id}/cancel", CancelAsync);
        return market;
    }

    private static async Task<IResult> ListAsync(
        SimulatorDbContext db,
        TimeProvider time,
        string? symbol,
        string? status,
        CancellationToken cancellationToken)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, cancellationToken);
        var query =
            from action in db.MarketCorporateActions.AsNoTracking()
            join instrument in db.MarketInstruments.AsNoTracking() on action.InstrumentId equals instrument.Id
            select new { action, instrument.Symbol, instrument.Currency };
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalised = MarketRules.Symbol(symbol);
            query = query.Where(row => row.Symbol == normalised);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<MarketCorporateActionStatus>(status, ignoreCase: true, out var wanted) || !Enum.IsDefined(wanted))
            {
                return ControlEndpoints.Refused("Status", "status must be Announced, Effective or Cancelled.");
            }

            query = query.Where(row => row.action.Status == wanted);
        }

        var rows = await query.OrderByDescending(row => row.action.Number).ToListAsync(cancellationToken);
        return Results.Ok(new MarketCorporateActionList(
            rows.Select(row => MarketCorporateActionItem.From(world.WorldId, row.action, row.Symbol, row.Currency, versions: null)).ToList()));
    }

    private static async Task<IResult> GetAsync(string id, SimulatorDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, cancellationToken);
        var found = await FindAsync(db, world, id, cancellationToken);
        return found is null ? Results.NotFound() : Results.Ok(await ItemAsync(db, world, found, cancellationToken));
    }

    private static async Task<IResult> AnnounceAsync(
        AnnounceCorporateActionRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, http.RequestAborted);
        var symbol = string.IsNullOrWhiteSpace(body.Symbol) ? "" : MarketRules.Symbol(body.Symbol);
        var instrument = await db.MarketInstruments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Symbol == symbol, http.RequestAborted);
        if (instrument is null)
        {
            return ControlEndpoints.Refused("Symbol", "symbol must be a simulated instrument of the market.");
        }

        if (!Enum.TryParse<MarketCorporateActionKind>(body.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind)
            || body.Kind!.All(char.IsAsciiDigit))
        {
            return ControlEndpoints.Refused("Kind", "kind must be Split, Consolidation or Bonus.");
        }

        var refusal = Terms(kind, body.RatioNew, body.RatioHeld, body.ExDate, world, time);
        if (refusal is not null)
        {
            return refusal;
        }

        var number = (await db.MarketCorporateActions.MaxAsync(row => (int?)row.Number, http.RequestAborted) ?? 0) + 1;
        var action = MarketCorporateAction.Announce(
            number, instrument.Id, kind, body.RatioNew!.Value, body.RatioHeld!.Value, body.ExDate!.Value, body.RecordDate, body.PaymentDate);
        db.MarketCorporateActions.Add(action);
        var realNow = time.GetUtcNow();
        MarketControlEndpoints.Log(db, http, world, realNow, ControlActions.CorporateActionAnnounced, body);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Another corporate action was announced at once. Retry.");
        }

        var actionId = MarketCorporateAction.ActionId(world.WorldId, number);
        return Results.Created($"/control/market/corporate-actions/{actionId}", new AnnouncedCorporateAction(actionId));
    }

    private static async Task<IResult> AmendAsync(
        string id,
        AmendCorporateActionRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, http.RequestAborted);
        var action = await FindAsync(db, world, id, http.RequestAborted);
        if (action is null)
        {
            return Results.NotFound();
        }

        if (action.Status != MarketCorporateActionStatus.Announced)
        {
            return ControlEndpoints.Refused("Status", $"The corporate action is {action.Status} and cannot be amended.");
        }

        var refusal = Terms(action.Kind, body.RatioNew, body.RatioHeld, body.ExDate, world, time);
        if (refusal is not null)
        {
            return refusal;
        }

        action.Amend(body.RatioNew!.Value, body.RatioHeld!.Value, body.ExDate!.Value, body.RecordDate, body.PaymentDate);
        return await SaveAsync(db, http, world, time, ControlActions.CorporateActionAmended, new { id, body.RatioNew, body.RatioHeld, body.ExDate }, action);
    }

    private static async Task<IResult> CancelAsync(string id, HttpContext http, SimulatorDbContext db, TimeProvider time)
    {
        var world = await MarketControlEndpoints.UpToDateAsync(db, time, http.RequestAborted);
        var action = await FindAsync(db, world, id, http.RequestAborted);
        if (action is null)
        {
            return Results.NotFound();
        }

        if (action.Status != MarketCorporateActionStatus.Announced)
        {
            return ControlEndpoints.Refused("Status", $"The corporate action is {action.Status} and cannot be cancelled.");
        }

        action.Cancel();
        return await SaveAsync(db, http, world, time, ControlActions.CorporateActionCancelled, new { id }, action);
    }

    /// <summary>R39, R40: the ratio fits the kind (R3) and the ex-date is after the current simulator day.</summary>
    private static IResult? Terms(MarketCorporateActionKind kind, int? ratioNew, int? ratioHeld, DateOnly? exDate, World world, TimeProvider time)
    {
        if (!MarketCorporateAction.FitsKind(kind, ratioNew, ratioHeld))
        {
            return ControlEndpoints.Refused("RatioNew",
                "ratioNew and ratioHeld must be whole numbers from 1 to 10000; a split has more new shares than held, a consolidation fewer.");
        }

        if (exDate is null || exDate <= MarketPricePath.Day(world.Now(time.GetUtcNow())))
        {
            return ControlEndpoints.Refused("ExDate", "exDate must be after the current simulator day.");
        }

        return null;
    }

    private static async Task<MarketCorporateAction?> FindAsync(SimulatorDbContext db, World world, string id, CancellationToken cancellationToken)
    {
        if (!MarketCorporateAction.TryReadNumber(id.Trim(), world.WorldId, out var number))
        {
            return null;
        }

        return await db.MarketCorporateActions.SingleOrDefaultAsync(row => row.Number == number, cancellationToken);
    }

    private static async Task<MarketCorporateActionItem> ItemAsync(
        SimulatorDbContext db,
        World world,
        MarketCorporateAction action,
        CancellationToken cancellationToken)
    {
        var instrument = await db.MarketInstruments.AsNoTracking().SingleAsync(row => row.Id == action.InstrumentId, cancellationToken);
        var versions = await db.MarketCorporateActionVersions.AsNoTracking()
            .Where(row => row.ActionId == action.Id)
            .OrderBy(row => row.Version)
            .ToListAsync(cancellationToken);
        return MarketCorporateActionItem.From(world.WorldId, action, instrument.Symbol, instrument.Currency, versions);
    }

    private static async Task<IResult> SaveAsync(
        SimulatorDbContext db,
        HttpContext http,
        World world,
        TimeProvider time,
        string logAction,
        object detail,
        MarketCorporateAction action)
    {
        MarketControlEndpoints.Log(db, http, world, time.GetUtcNow(), logAction, detail);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The corporate action was changed by another write. Read it again and retry.");
        }

        return Results.Ok(await ItemAsync(db, world, action, http.RequestAborted));
    }
}

public sealed record AnnounceCorporateActionRequest(
    string? Symbol,
    string? Kind,
    int? RatioNew,
    int? RatioHeld,
    DateOnly? ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate);

public sealed record AmendCorporateActionRequest(int? RatioNew, int? RatioHeld, DateOnly? ExDate, DateOnly? RecordDate, DateOnly? PaymentDate);

public sealed record AnnouncedCorporateAction(string Id);

public sealed record MarketCorporateActionVersionItem(
    int Version,
    string Status,
    int RatioNew,
    int RatioHeld,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate);

public sealed record MarketCorporateActionItem(
    string Id,
    string Symbol,
    string Currency,
    string Kind,
    int RatioNew,
    int RatioHeld,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate,
    string Status,
    int Version,
    IReadOnlyList<MarketCorporateActionVersionItem>? Versions)
{
    public static MarketCorporateActionItem From(
        Guid worldId,
        MarketCorporateAction action,
        string symbol,
        string currency,
        IReadOnlyList<MarketCorporateActionVersion>? versions) =>
        new(
            MarketCorporateAction.ActionId(worldId, action.Number),
            symbol,
            currency.Trim(),
            action.Kind.ToString(),
            action.RatioNew,
            action.RatioHeld,
            action.ExDate,
            action.RecordDate,
            action.PaymentDate,
            action.Status.ToString(),
            action.Version,
            versions?.Select(version => new MarketCorporateActionVersionItem(
                version.Version, version.Status.ToString(), version.RatioNew, version.RatioHeld, version.ExDate, version.RecordDate,
                version.PaymentDate)).ToList());
}

public sealed record MarketCorporateActionList(IReadOnlyList<MarketCorporateActionItem> Items);
