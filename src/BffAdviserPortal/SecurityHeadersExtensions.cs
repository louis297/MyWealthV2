namespace MyWealthV2.BffAdviserPortal;

public static class SecurityHeadersExtensions
{
    public static WebApplication UseBffSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
            await next();
        });

        return app;
    }
}
