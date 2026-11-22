using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Market;

/// <summary>Simulated instruments R16, R19, R22: the simulator market on the control surface. Every route brings
/// the market up to date first (R18).</summary>
public static class MarketControlEndpoints
{
    public static RouteGroupBuilder MapMarketControlEndpoints(this RouteGroupBuilder control)
    {
        var market = control.MapGroup("/market").WithTags("Market");
        market.MapGet("/instruments", GetInstrumentsAsync);
        market.MapPost("/instruments", AddInstrumentAsync);
        market.MapPost("/instruments/{symbol}/price", SetPriceAsync);
        market.MapPost("/instruments/{symbol}/daily-move", SetDailyMoveAsync);
        market.MapGet("/instruments/{symbol}/prices", GetPricesAsync);
        market.MapMarketCorporateActionEndpoints();
        return control;
    }

    private static async Task<IResult> GetInstrumentsAsync(SimulatorDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        await UpToDateAsync(db, time, cancellationToken);
        var instruments = await db.MarketInstruments.AsNoTracking()
            .OrderBy(instrument => instrument.Symbol)
            .ToListAsync(cancellationToken);
        return Results.Ok(new MarketInstrumentList(instruments.Select(MarketInstrumentItem.From).ToList()));
    }

    private static async Task<IResult> AddInstrumentAsync(
        AddMarketInstrumentRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!MarketRules.IsSymbol(body.Symbol))
        {
            return ControlEndpoints.Refused("Symbol",
                $"symbol must be 5 to 32 letters, digits, dots and hyphens, ending in {MarketRules.Suffix}.");
        }

        if (!MarketRules.IsName(body.Name))
        {
            return ControlEndpoints.Refused("Name", "name must be 1 to 200 characters.");
        }

        if (!MarketRules.IsCurrency(body.Currency))
        {
            return ControlEndpoints.Refused("Currency", "currency must be three upper-case letters.");
        }

        if (!MarketRules.IsPrice(body.Price))
        {
            return ControlEndpoints.Refused("Price", "price must be above zero with at most 4 decimal places.");
        }

        var dailyMove = body.DailyMovePercent ?? MarketRules.DefaultDailyMovePercent;
        if (!MarketRules.IsDailyMove(dailyMove))
        {
            return ControlEndpoints.Refused("DailyMovePercent", "dailyMovePercent must be 0 to 20 with at most 2 decimal places.");
        }

        var symbol = MarketRules.Symbol(body.Symbol!);
        var world = await UpToDateAsync(db, time, http.RequestAborted);
        if (await db.MarketInstruments.AnyAsync(instrument => instrument.Symbol == symbol, http.RequestAborted))
        {
            return ControlEndpoints.Refused("Symbol", "An instrument with this symbol already exists.");
        }

        var realNow = time.GetUtcNow();
        var instrument = MarketInstrument.Add(
            symbol, body.Name!.Trim(), body.Currency!, body.Price!.Value, dailyMove, MarketPricePath.Day(world.Now(realNow)), typed: true);
        db.MarketInstruments.Add(instrument);
        Log(db, http, world, realNow, ControlActions.MarketInstrumentAdded, body);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException)
        {
            // Another add of the same symbol won the unique index.
            return ControlEndpoints.Refused("Symbol", "An instrument with this symbol already exists.");
        }

        return Results.Created(
            $"/control/market/instruments/{Uri.EscapeDataString(instrument.Symbol)}", MarketInstrumentItem.From(instrument));
    }

    private static async Task<IResult> SetPriceAsync(
        string symbol,
        SetMarketPriceRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var world = await UpToDateAsync(db, time, http.RequestAborted);
        var instrument = await FindAsync(db, symbol, http.RequestAborted);
        if (instrument is null)
        {
            return Results.NotFound();
        }

        if (!MarketRules.IsPrice(body.Price))
        {
            return ControlEndpoints.Refused("Price", "price must be above zero with at most 4 decimal places.");
        }

        var today = await db.MarketPrices
            .SingleAsync(price => price.InstrumentId == instrument.Id && price.PriceDate == instrument.PriceDate, http.RequestAborted);
        today.Set(body.Price!.Value);
        instrument.SetPrice(body.Price.Value);
        return await SaveAsync(db, http, world, time, ControlActions.MarketPriceSet, new { symbol = instrument.Symbol, body.Price }, instrument);
    }

    private static async Task<IResult> SetDailyMoveAsync(
        string symbol,
        SetMarketDailyMoveRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var world = await UpToDateAsync(db, time, http.RequestAborted);
        var instrument = await FindAsync(db, symbol, http.RequestAborted);
        if (instrument is null)
        {
            return Results.NotFound();
        }

        if (!MarketRules.IsDailyMove(body.DailyMovePercent))
        {
            return ControlEndpoints.Refused("DailyMovePercent", "dailyMovePercent must be 0 to 20 with at most 2 decimal places.");
        }

        instrument.SetDailyMove(body.DailyMovePercent!.Value);
        return await SaveAsync(
            db, http, world, time, ControlActions.MarketDailyMoveSet, new { symbol = instrument.Symbol, body.DailyMovePercent }, instrument);
    }

    private static async Task<IResult> GetPricesAsync(
        string symbol,
        SimulatorDbContext db,
        TimeProvider time,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        await UpToDateAsync(db, time, cancellationToken);
        var instrument = await FindAsync(db, symbol, cancellationToken);
        if (instrument is null)
        {
            return Results.NotFound();
        }

        var size = Math.Clamp(pageSize ?? ControlEndpoints.DefaultPageSize, 1, ControlEndpoints.MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var prices = db.MarketPrices.AsNoTracking().Where(price => price.InstrumentId == instrument.Id);
        var total = await prices.CountAsync(cancellationToken);
        var rows = await prices
            .OrderByDescending(price => price.PriceDate)
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        var items = rows.Select(price => new MarketPriceItem(price.PriceDate, price.Price, price.IsSet)).ToList();
        return Results.Ok(new MarketPricePage(items, number, size, total));
    }

    /// <summary>R18: the world, with every instrument priced up to its current simulator day.</summary>
    internal static async Task<World> UpToDateAsync(SimulatorDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        await MarketPricePath.BringUpToDateAsync(db, world.Seed, MarketPricePath.Day(world.Now(time.GetUtcNow())), cancellationToken);
        return world;
    }

    private static Task<MarketInstrument?> FindAsync(SimulatorDbContext db, string symbol, CancellationToken cancellationToken)
    {
        var normalised = symbol.Trim().ToUpperInvariant();
        return db.MarketInstruments.SingleOrDefaultAsync(instrument => instrument.Symbol == normalised, cancellationToken);
    }

    private static async Task<IResult> SaveAsync(
        SimulatorDbContext db,
        HttpContext http,
        World world,
        TimeProvider time,
        string action,
        object detail,
        MarketInstrument instrument)
    {
        Log(db, http, world, time.GetUtcNow(), action, detail);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The instrument was changed by another write. Read it again and retry.");
        }

        return Results.Ok(MarketInstrumentItem.From(instrument));
    }

    internal static void Log(SimulatorDbContext db, HttpContext http, World world, DateTimeOffset realNow, string action, object body) =>
        db.ControlLog.Add(ControlLogEntry.Create(
            realNow,
            world.Now(realNow),
            ControlEndpoints.Actor(http.User),
            action,
            JsonSerializer.Serialize(body, body.GetType(), ControlEndpoints.DetailJson)));
}

public sealed record AddMarketInstrumentRequest(string? Symbol, string? Name, string? Currency, decimal? Price, decimal? DailyMovePercent);

public sealed record SetMarketPriceRequest(decimal? Price);

public sealed record SetMarketDailyMoveRequest(decimal? DailyMovePercent);

public sealed record MarketInstrumentItem(
    string Symbol,
    string Name,
    string Currency,
    decimal Price,
    DateOnly PriceDate,
    decimal DailyMovePercent)
{
    public static MarketInstrumentItem From(MarketInstrument instrument) => new(
        instrument.Symbol,
        instrument.Name,
        instrument.Currency.Trim(),
        instrument.Price,
        instrument.PriceDate,
        instrument.DailyMovePercent);
}

public sealed record MarketInstrumentList(IReadOnlyList<MarketInstrumentItem> Items);

public sealed record MarketPriceItem(DateOnly Date, decimal Price, bool Set);

public sealed record MarketPricePage(IReadOnlyList<MarketPriceItem> Items, int Page, int PageSize, int TotalCount);
