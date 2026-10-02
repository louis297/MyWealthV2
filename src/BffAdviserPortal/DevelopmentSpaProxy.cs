using System.Net;
using Microsoft.Extensions.Primitives;

namespace MyWealthV2.BffAdviserPortal;

public static class DevelopmentSpaProxy
{
    private static readonly string[] ModulePrefixes =
    [
        "/src",
        "/@vite",
        "/@fs",
        "/@id",
        "/@react-refresh",
        "/node_modules"
    ];

    public static void Map(WebApplication app, string spaOrigin)
    {
        var spaBase = new Uri(spaOrigin);
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.Zero
        })
        {
            BaseAddress = spaBase,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        app.Lifetime.ApplicationStopping.Register(client.Dispose);

        RequestDelegate forward = http => ForwardAsync(http, client, spaBase);
        foreach (var prefix in ModulePrefixes)
        {
            app.Map(prefix, forward);
            app.Map(prefix + "/{**remainder}", forward);
        }

        app.MapFallback(async (HttpContext http) =>
        {
            if (IsReserved(http.Request.Path))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await forward(http);
        });
    }

    private static bool IsReserved(PathString path) =>
        path.StartsWithSegments("/api")
        || path.StartsWithSegments("/bff")
        || path.StartsWithSegments("/signin-oidc")
        || path.StartsWithSegments("/signout-callback-oidc")
        || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/alive");

    private static async Task ForwardAsync(HttpContext http, HttpClient spaClient, Uri spaBase)
    {
        if (http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var target = new Uri(spaBase, http.Request.Path + http.Request.QueryString);
        using var outbound = new HttpRequestMessage(new HttpMethod(http.Request.Method), target);
        outbound.Version = HttpVersion.Version11;
        outbound.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        // Vite uses Host to resolve dev modules. This is the dev server endpoint.
        outbound.Headers.Host = spaBase.Authority;
        if (http.Request.Headers.TryGetValue("Accept", out var accept) && !StringValues.IsNullOrEmpty(accept))
        {
            outbound.Headers.TryAddWithoutValidation("Accept", accept.ToArray());
        }

        using var inbound = await spaClient.SendAsync(
            outbound,
            HttpCompletionOption.ResponseHeadersRead,
            http.RequestAborted);
        http.Response.StatusCode = (int)inbound.StatusCode;
        if (inbound.Content.Headers.ContentType is { } contentType)
        {
            http.Response.ContentType = contentType.ToString();
        }

        await inbound.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
    }
}
