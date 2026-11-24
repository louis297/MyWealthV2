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
            return ControlEndpoints.Refused("Kind", "kind must be Split, Consolidation, Bonus or Dividend.");
        }

        if (kind == MarketCorporateActionKind.Dividend)
        {
            return await AnnounceDividendAsync(body, instrument.Id, http, db, world, time);
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

        if (action.Kind == MarketCorporateActionKind.Dividend)
        {
            // Dividends R55: amend replaces the terms and the dates.
            var (terms, dividendRefusal) = DividendTerms(body.RatioNew, body.RatioHeld, body.AmountPerShare, body.TaxSource,
                body.ImputationCreditPerShare, body.ForeignTaxRatePercent, body.ReinvestmentPrice, body.ExDate, body.PaymentDate, world, time);
            if (dividendRefusal is not null)
            {
                return dividendRefusal;
            }

            action.AmendDividend(terms!, body.ExDate!.Value, body.RecordDate, body.PaymentDate!.Value);
            return await SaveAsync(db, http, world, time, ControlActions.CorporateActionAmended, new { id, body.AmountPerShare, body.ExDate }, action);
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

    /// <summary>Dividends R54: a dividend with its terms, no ratio, and a payment date.</summary>
    private static async Task<IResult> AnnounceDividendAsync(
        AnnounceCorporateActionRequest body,
        int instrumentId,
        HttpContext http,
        SimulatorDbContext db,
        World world,
        TimeProvider time)
    {
        var (terms, refusal) = DividendTerms(body.RatioNew, body.RatioHeld, body.AmountPerShare, body.TaxSource,
            body.ImputationCreditPerShare, body.ForeignTaxRatePercent, body.ReinvestmentPrice, body.ExDate, body.PaymentDate, world, time);
        if (refusal is not null)
        {
            return refusal;
        }

        var number = (await db.MarketCorporateActions.MaxAsync(row => (int?)row.Number, http.RequestAborted) ?? 0) + 1;
        db.MarketCorporateActions.Add(MarketCorporateAction.AnnounceDividend(
            number, instrumentId, terms!, body.ExDate!.Value, body.RecordDate, body.PaymentDate!.Value));
        MarketControlEndpoints.Log(db, http, world, time.GetUtcNow(), ControlActions.CorporateActionAnnounced, body);
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

    /// <summary>Dividends R3, R54: the terms of a dividend, or the refusal of the first field that breaks them.</summary>
    private static (MarketDividendTerms? Terms, IResult? Refusal) DividendTerms(
        int? ratioNew,
        int? ratioHeld,
        decimal? amountPerShare,
        string? taxSource,
        decimal? imputationCreditPerShare,
        decimal? foreignTaxRatePercent,
        decimal? reinvestmentPrice,
        DateOnly? exDate,
        DateOnly? paymentDate,
        World world,
        TimeProvider time)
    {
        static bool Scale(decimal value, int decimals) => decimal.Round(value, decimals) == value;

        if (ratioNew is not null || ratioHeld is not null)
        {
            return (null, ControlEndpoints.Refused("RatioNew", "A dividend has no ratio."));
        }

        if (amountPerShare is not { } amount || amount <= 0m || amount > 1_000_000m || !Scale(amount, 8))
        {
            return (null, ControlEndpoints.Refused("AmountPerShare",
                "amountPerShare must be above 0, at most 1000000, with at most 8 decimal places."));
        }

        MarketDividendTaxSource? source = taxSource switch
        {
            "NewZealand" => MarketDividendTaxSource.NewZealand,
            "Foreign" => MarketDividendTaxSource.Foreign,
            _ => null
        };
        if (source is null)
        {
            return (null, ControlEndpoints.Refused("TaxSource", "taxSource must be NewZealand or Foreign."));
        }

        var credit = imputationCreditPerShare ?? 0m;
        if (credit < 0m || credit > amount || !Scale(credit, 8) || (credit > 0m && source != MarketDividendTaxSource.NewZealand))
        {
            return (null, ControlEndpoints.Refused("ImputationCreditPerShare",
                "imputationCreditPerShare must be 0 or more and not above amountPerShare; above 0 only for a New Zealand company."));
        }

        var rate = foreignTaxRatePercent ?? 0m;
        if (rate is < 0m or >= 100m || !Scale(rate, 2) || (rate > 0m && source != MarketDividendTaxSource.Foreign))
        {
            return (null, ControlEndpoints.Refused("ForeignTaxRatePercent",
                "foreignTaxRatePercent must be 0 or more and below 100; above 0 only for a foreign company."));
        }

        if (reinvestmentPrice is { } price && (price <= 0m || !Scale(price, 8)))
        {
            return (null, ControlEndpoints.Refused("ReinvestmentPrice", "reinvestmentPrice must be above 0, with at most 8 decimal places."));
        }

        if (exDate is null || exDate <= MarketPricePath.Day(world.Now(time.GetUtcNow())))
        {
            return (null, ControlEndpoints.Refused("ExDate", "exDate must be after the current simulator day."));
        }

        if (paymentDate is null || paymentDate < exDate)
        {
            return (null, ControlEndpoints.Refused("PaymentDate", "paymentDate is required for a dividend and not before exDate."));
        }

        return (new MarketDividendTerms(amount, source.Value, credit, rate, reinvestmentPrice), null);
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
    DateOnly? PaymentDate,
    decimal? AmountPerShare = null,
    string? TaxSource = null,
    decimal? ImputationCreditPerShare = null,
    decimal? ForeignTaxRatePercent = null,
    decimal? ReinvestmentPrice = null);

public sealed record AmendCorporateActionRequest(
    int? RatioNew,
    int? RatioHeld,
    DateOnly? ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate,
    decimal? AmountPerShare = null,
    string? TaxSource = null,
    decimal? ImputationCreditPerShare = null,
    decimal? ForeignTaxRatePercent = null,
    decimal? ReinvestmentPrice = null);

public sealed record AnnouncedCorporateAction(string Id);

public sealed record MarketCorporateActionVersionItem(
    int Version,
    string Status,
    int? RatioNew,
    int? RatioHeld,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate);

public sealed record MarketCorporateActionItem(
    string Id,
    string Symbol,
    string Currency,
    string Kind,
    int? RatioNew,
    int? RatioHeld,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate,
    string Status,
    int Version,
    IReadOnlyList<MarketCorporateActionVersionItem>? Versions,
    decimal? AmountPerShare = null,
    string? TaxSource = null,
    decimal? ImputationCreditPerShare = null,
    decimal? ForeignTaxRatePercent = null,
    decimal? ReinvestmentPrice = null,
    DateOnly? PaidOn = null)
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
                version.PaymentDate)).ToList(),
            action.AmountPerShare,
            action.TaxSource?.ToString(),
            action.ImputationCreditPerShare,
            action.ForeignTaxRatePercent,
            action.ReinvestmentPrice,
            action.PaidOn);
}

public sealed record MarketCorporateActionList(IReadOnlyList<MarketCorporateActionItem> Items);
