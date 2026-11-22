using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Broker;

/// <summary>Orders R55, R56: the operator's hand on the simulator broker. Every write is logged (simulator R28).</summary>
public static class BrokerControlEndpoints
{
    public static RouteGroupBuilder MapBrokerControlEndpoints(this RouteGroupBuilder control)
    {
        var broker = control.MapGroup("/broker");
        broker.MapPost("/order-mode", SetOrderModeAsync);
        broker.MapPost("/slippage", SetSlippageAsync);
        return control;
    }

    /// <summary>R55: how the broker answers new orders.</summary>
    private static async Task<IResult> SetOrderModeAsync(
        BrokerOrderModeRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!TryName<BrokerOrderMode>(body.Mode, out var mode))
        {
            return ControlEndpoints.Refused("Mode", "mode must be Fill, Hold or Reject.");
        }

        if (body.FillPercent is not null && !BrokerRules.IsFillPercent(body.FillPercent))
        {
            return ControlEndpoints.Refused("FillPercent", "fillPercent must be a whole number from 1 to 100.");
        }

        BrokerRejectReason? reason = null;
        if (mode == BrokerOrderMode.Reject)
        {
            if (!TryName<BrokerRejectReason>(body.Reason, out var named))
            {
                return ControlEndpoints.Refused(
                    "Reason", $"reason must be one of {string.Join(", ", Enum.GetNames<BrokerRejectReason>())} with Reject.");
            }

            reason = named;
        }

        return await WriteAsync(http, db, time, ControlActions.BrokerOrderModeChanged, body,
            settings => settings.SetMode(mode, body.FillPercent ?? BrokerRules.DefaultFillPercent, reason));
    }

    /// <summary>R56: the slippage of later fills.</summary>
    private static async Task<IResult> SetSlippageAsync(
        BrokerSlippageRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!BrokerRules.IsSlippage(body.SlippagePercent))
        {
            return ControlEndpoints.Refused("SlippagePercent", "slippagePercent must be 0 to 5 with at most 2 decimal places.");
        }

        return await WriteAsync(http, db, time, ControlActions.BrokerSlippageSet, body,
            settings => settings.SetSlippage(body.SlippagePercent!.Value));
    }

    /// <summary>One write on the settings row: brings the books up to date (R52), applies it, appends the log row and
    /// saves both. A concurrent write on the row returns 409.</summary>
    private static async Task<IResult> WriteAsync(
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time,
        string action,
        object body,
        Action<BrokerSettings> apply)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, http.RequestAborted);
        var settings = await db.BrokerSettings.SingleAsync(http.RequestAborted);
        apply(settings);
        MarketControlEndpoints.Log(db, http, world, time.GetUtcNow(), action, body);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The broker settings were changed by another write. Read them again and retry.");
        }

        return Results.Ok(BrokerSettingsItem.From(settings));
    }

    /// <summary>An exact name of <typeparamref name="T"/>; a number is not a name.</summary>
    private static bool TryName<T>(string? value, out T named)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out named) && Enum.GetNames<T>().Contains(value, StringComparer.Ordinal);
}

public sealed record BrokerOrderModeRequest(string? Mode, int? FillPercent, string? Reason);

public sealed record BrokerSlippageRequest(decimal? SlippagePercent);

/// <summary>R64: the broker's settings.</summary>
public sealed record BrokerSettingsItem(string Mode, int FillPercent, string? RejectReason, decimal SlippagePercent, int SettlementDays)
{
    public static BrokerSettingsItem From(BrokerSettings settings) => new(
        settings.Mode.ToString(),
        settings.FillPercent,
        settings.RejectReason?.ToString(),
        settings.SlippagePercent,
        settings.SettlementDays);
}
