using Microsoft.Net.Http.Headers;

namespace MyWealthV2.BffCustomerPortal;

// Paths that belong to the BFF itself. They never reach the SPA, whether it is served by the
// Vite dev server (DevelopmentSpaProxy) or from the build in wwwroot (SpaStaticFiles).
public static class SpaReservedPaths
{
    public static bool IsReserved(PathString path) =>
        path.StartsWithSegments("/api")
        || path.StartsWithSegments("/bff")
        || path.StartsWithSegments("/signin-oidc")
        || path.StartsWithSegments("/signout-callback-oidc")
        || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/alive");
}

// Serves the built Customer Portal from wwwroot when there is no Vite dev server
// (bff-customer-portal.md amendment 2026-11-12, P2-P10).
public static class SpaStaticFiles
{
    private const string IndexFile = "index.html";
    private const string NoCache = "no-cache";
    private const string Immutable = "public, max-age=31536000, immutable";

    public static WebApplication UseSpaStaticFiles(this WebApplication app)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path;
                if (path.StartsWithSegments("/assets"))
                {
                    context.Context.Response.Headers[HeaderNames.CacheControl] = Immutable;
                }
                else if (path.Equals("/" + IndexFile, StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers[HeaderNames.CacheControl] = NoCache;
                }
            }
        });

        return app;
    }

    // MapFallback's default pattern only matches paths whose last segment has no extension.
    public static WebApplication MapSpaFallback(this WebApplication app)
    {
        app.MapFallback(async (HttpContext http, IWebHostEnvironment environment) =>
        {
            var method = http.Request.Method;
            if (!(HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
                || SpaReservedPaths.IsReserved(http.Request.Path))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var index = environment.WebRootFileProvider.GetFileInfo(IndexFile);
            if (!index.Exists)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            http.Response.ContentType = "text/html; charset=utf-8";
            http.Response.Headers[HeaderNames.CacheControl] = NoCache;
            http.Response.ContentLength = index.Length;
            if (HttpMethods.IsHead(method))
            {
                return;
            }

            await http.Response.SendFileAsync(index, http.RequestAborted);
        });

        return app;
    }
}
