using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Hosting;

namespace MyWealthV2.Simulator.Party;

/// <summary>
/// Simulator spec R27, R30–R33: the surface the platform calls, on the party port only, behind the machine
/// credential the simulator owns. Each party adds its routes under <c>/party/{party}/…</c>.
/// </summary>
public static class PartyEndpoints
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ApiKeyKey = "Simulator:PartyApiKey";
    public const int MinimumKeyLength = 32;

    /// <summary>R31: in every environment, the host refuses to start without a key of at least 32 characters.</summary>
    public static string ReadPartyApiKey(IConfiguration configuration)
    {
        var key = configuration[ApiKeyKey];
        if (string.IsNullOrEmpty(key) || key.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"{ApiKeyKey} must be set to a key of at least {MinimumKeyLength} characters.");
        }

        return key;
    }

    public static WebApplication MapPartyEndpoints(this WebApplication app, string partyApiKey)
    {
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(partyApiKey));
        var party = app.MapGroup(SurfacePorts.PartyPrefix)
            .AllowAnonymous()
            .ExcludeFromDescription()
            .AddEndpointFilter(async (context, next) =>
            {
                // R30: the credential first, compared in constant time; no detail in the answer.
                var presented = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
                var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
                if (presented.Length == 0 || !CryptographicOperations.FixedTimeEquals(actual, expected))
                {
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);
                }

                // R27: then the outage.
                var db = context.HttpContext.RequestServices.GetRequiredService<SimulatorDbContext>();
                var outage = await db.Worlds.AsNoTracking()
                    .Select(world => world.PartyOutage)
                    .SingleAsync(context.HttpContext.RequestAborted);
                if (outage)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                return await next(context);
            });

        party.MapGet("/status", GetStatusAsync);

        return app;
    }

    /// <summary>R32: the world id and simulator now. No platform value (R33).</summary>
    private static async Task<IResult> GetStatusAsync(
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        return Results.Ok(new PartyStatus(world.WorldId, world.Now(time.GetUtcNow())));
    }
}

public sealed record PartyStatus(Guid WorldId, DateTimeOffset Now);
