using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using MyWealthV2.Shared;

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
        http.Request.EnableBuffering();
        var accessToken = await http.GetTokenAsync("access_token");
        var inbound = await SendAsync(http, transport, target, accessToken);
        if (inbound.StatusCode == HttpStatusCode.Unauthorized)
        {
            inbound.Dispose();
            var refreshed = await TryRefreshAsync(http);
            if (refreshed is null)
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                await WriteProblem(http, StatusCodes.Status401Unauthorized, "Unauthorized",
                    "https://tools.ietf.org/html/rfc9110#section-15.5.2");
                return;
            }

            if (http.Request.Body.CanSeek)
            {
                http.Request.Body.Position = 0;
            }

            inbound = await SendAsync(http, transport, target, refreshed);
        }

        using (inbound)
        {
            http.Response.StatusCode = (int)inbound.StatusCode;
            if (inbound.Content.Headers.ContentType is { } contentType)
            {
                http.Response.ContentType = contentType.ToString();
            }

            await inbound.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpContext http,
        IWebApiTransport transport,
        string target,
        string? accessToken)
    {
        var outbound = new HttpRequestMessage(new HttpMethod(http.Request.Method), target);
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

        if (!string.IsNullOrEmpty(accessToken))
        {
            outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await transport.SendAsync(outbound, http.RequestAborted);
    }

    private static async Task<string?> TryRefreshAsync(HttpContext http)
    {
        var refreshToken = await http.GetTokenAsync("refresh_token");
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var oidc = http.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var authority = (configuration["Authentication:Authority"] ?? oidc.Authority ?? "").TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Post, authority + "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = Services.AdviserPortal,
                ["client_secret"] = configuration["Authentication:ClientSecret"]
            })
        };

        using var response = await oidc.Backchannel.SendAsync(message, http.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(http.RequestAborted));
        var accessToken = json.RootElement.GetProperty("access_token").GetString();
        var nextRefresh = json.RootElement.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString()
            : refreshToken;
        var idToken = json.RootElement.TryGetProperty("id_token", out var id)
            ? id.GetString()
            : await http.GetTokenAsync("id_token");
        var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
            ? seconds
            : 900;

        var authenticated = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (authenticated.Principal is null || authenticated.Properties is null || string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        authenticated.Properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = accessToken },
            new AuthenticationToken { Name = "refresh_token", Value = nextRefresh ?? refreshToken },
            new AuthenticationToken { Name = "id_token", Value = idToken ?? "" },
            new AuthenticationToken { Name = "token_type", Value = "Bearer" },
            new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToString("o") }
        ]);
        authenticated.Properties.IsPersistent = false;
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            authenticated.Principal,
            authenticated.Properties);
        return accessToken;
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
