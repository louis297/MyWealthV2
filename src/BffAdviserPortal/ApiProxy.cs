using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace MyWealthV2.BffAdviserPortal;

public static class ApiProxy
{
    public const string RequestHeaderName = "X-MyWealth-Request";

    private static readonly string[] Prefixes =
    [
        "/users",
        "/tenants",
        "/currencies",
        "/accounts",
        "/instruments",
        "/transactions"
    ];

    public static WebApplication MapBffApi(this WebApplication app)
    {
        app.Map("/api/{**remainder}", ProxyAsync);
        return app;
    }

    public static bool IsAllowed(string path)
    {
        foreach (var prefix in Prefixes)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsMutating(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);

    private static async Task ProxyAsync(HttpContext http, string? remainder, IWebApiTransport transport)
    {
        var path = "/" + (remainder ?? "").TrimStart('/');
        if (!IsAllowed(path))
        {
            await WriteProblem(http, StatusCodes.Status404NotFound, "Not Found",
                "https://tools.ietf.org/html/rfc9110#section-15.5.5");
            return;
        }

        if (IsMutating(http.Request.Method) &&
            !string.Equals(http.Request.Headers[RequestHeaderName], "1", StringComparison.Ordinal))
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (http.User.Identity?.IsAuthenticated != true)
        {
            await WriteProblem(http, StatusCodes.Status401Unauthorized, "Unauthorized",
                "https://tools.ietf.org/html/rfc9110#section-15.5.2");
            return;
        }

        var baseAddress = http.RequestServices.GetRequiredService<IConfiguration>()["Api:BaseAddress"]
            ?? "https+http://webapi";
        var target = baseAddress.TrimEnd('/') + path + http.Request.QueryString;
        using var outbound = new HttpRequestMessage(new HttpMethod(http.Request.Method), target);

        http.Request.EnableBuffering();
        if (http.Request.ContentLength is > 0 || http.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            outbound.Content = new StreamContent(http.Request.Body);
            if (!string.IsNullOrEmpty(http.Request.ContentType))
            {
                outbound.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(http.Request.ContentType);
            }
        }

        if (http.Request.Headers.TryGetValue("Accept", out var accept) && !StringValues.IsNullOrEmpty(accept))
        {
            outbound.Headers.TryAddWithoutValidation("Accept", accept.ToArray());
        }

        if (http.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey) &&
            !StringValues.IsNullOrEmpty(idempotencyKey))
        {
            outbound.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToArray());
        }

        var accessToken = await http.GetTokenAsync("access_token");
        if (!string.IsNullOrEmpty(accessToken))
        {
            outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var inbound = await transport.SendAsync(outbound, http.RequestAborted);
        http.Response.StatusCode = (int)inbound.StatusCode;
        if (inbound.Content.Headers.ContentType is { } contentType)
        {
            http.Response.ContentType = contentType.ToString();
        }

        await inbound.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
    }

    private static Task WriteProblem(HttpContext http, int status, string title, string type)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type
        });
    }
}
