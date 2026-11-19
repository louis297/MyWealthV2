namespace MyWealthV2.Simulator.Hosting;

/// <summary>
/// Simulator spec R11, R12: the control surface and the party surface never share a port. The check reads the
/// connection's local port, as the health port does, so a reverse proxy that forwards only the control port can
/// never reach a party route by mistake.
/// </summary>
public static class SurfacePorts
{
    public const string PartyPortKey = "Simulator:PartyPort";
    public const int DefaultPartyPort = 8082;
    public const string PartyPrefix = "/party";

    public static int ReadPartyPort(IConfiguration configuration) =>
        configuration.GetValue(PartyPortKey, DefaultPartyPort);

    /// <summary>
    /// Answers 404 to a party path on any port but the party port, and to any other path on the party port.
    /// Outside Development the health port answers only <c>/health</c> and <c>/alive</c>. In Development the
    /// control surface answers on every port but the party port (Aspire's http and https endpoints).
    /// </summary>
    public static IApplicationBuilder UseSurfacePorts(this WebApplication app)
    {
        var partyPort = ReadPartyPort(app.Configuration);
        var healthPort = app.Environment.IsDevelopment()
            ? (int?)null
            : Microsoft.Extensions.Hosting.Extensions.ReadHealthPort(app.Configuration);

        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var port = context.Connection.LocalPort;
            var allowed = IsHealthPath(path)
                || (path.StartsWithSegments(PartyPrefix)
                    ? port == partyPort
                    : port != partyPort && port != healthPort);

            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }

    // ServiceDefaults decides on which port these answer (production-hosting R12, R13).
    private static bool IsHealthPath(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/alive");
}
