using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyWealthV2.Shared;

namespace MyWealthV2.Simulator.Control;

/// <summary>
/// Simulator spec R16–R19: OIDC sign-in against identity with client <c>simulator</c>, then an encrypted
/// HttpOnly cookie that lasts 8 hours from sign-in. The host keeps no token.
/// </summary>
public static class SimulatorAuthentication
{
    public const string SessionCookieName = "__Host-simulator";
    public const string OperatorPolicy = "SimulatorOperator";
    public const string OperatorRole = "simulatorOperator";
    public const string NotAllowedPath = "/not-allowed";
    public const string ControlPrefix = "/control";

    private const string ChallengeScheme = "SimulatorChallenge";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static WebApplicationBuilder AddSimulatorAuthentication(this WebApplicationBuilder builder)
    {
        var development = builder.Environment.IsDevelopment();

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = ChallengeScheme;
                options.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            // R18: control routes answer 401 without a session; pages go to the sign-in.
            .AddPolicyScheme(ChallengeScheme, displayName: null, options =>
                options.ForwardDefaultSelector = context => IsControl(context.Request)
                    ? CookieAuthenticationDefaults.AuthenticationScheme
                    : OpenIdConnectDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.Path = "/";
                options.Cookie.Domain = null;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = SessionLifetime;
                options.SlidingExpiration = false;
                options.AccessDeniedPath = NotAllowedPath;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (IsControl(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    }
                    else
                    {
                        context.Response.Redirect(NotAllowedPath);
                    }

                    return Task.CompletedTask;
                };
                // R17, second line: a cookie that does not carry the operator role is not a session.
                options.Events.OnValidatePrincipal = context =>
                {
                    if (!IsOperator(context.Principal))
                    {
                        context.RejectPrincipal();
                    }

                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var authority = builder.Configuration["Authentication:Authority"];
                options.Authority = authority;
                options.ClientId = Services.Simulator;
                options.ClientSecret = builder.Configuration["Authentication:ClientSecret"];
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";
                options.GetClaimsFromUserInfoEndpoint = false;
                options.RequireHttpsMetadata = authority is not null &&
                    authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                options.MapInboundClaims = false;
                options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
                options.Events.OnTokenValidated = RejectOtherRolesAsync;
                options.Events.OnTicketReceived = context =>
                {
                    // R16: 8 hours from sign-in, not sliding, gone when the browser closes.
                    context.Properties ??= new AuthenticationProperties();
                    context.Properties.IsPersistent = false;
                    context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.Add(SessionLifetime);
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(OperatorPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("role", OperatorRole));

        return builder;
    }

    /// <summary>
    /// R17: identity's allow-list already refuses every other role on client <c>simulator</c>. This is the
    /// second line: a token for another role issues no session and shows the "not allowed" page.
    /// </summary>
    public static Task RejectOtherRolesAsync(TokenValidatedContext context)
    {
        if (!IsOperator(context.Principal))
        {
            context.HandleResponse();
            context.Response.Redirect(NotAllowedPath);
        }

        return Task.CompletedTask;
    }

    public static WebApplication MapSimulatorSession(this WebApplication app)
    {
        app.MapGet(NotAllowedPath, (HttpContext http) => Results.Content(
                $"""
                <!doctype html>
                <html lang="en">
                <head><meta charset="utf-8"><title>Not allowed</title></head>
                <body>
                <h1>Not allowed</h1>
                <p>Only the simulator operator can use the simulator.</p>
                <p><a href="{EndSessionUrl(http)}">Sign out</a></p>
                </body>
                </html>
                """,
                "text/html"))
            .AllowAnonymous()
            .ExcludeFromDescription();

        app.MapPost("/signout", async (HttpContext http) =>
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect(EndSessionUrl(http));
            })
            .RequireAuthorization(OperatorPolicy)
            .ExcludeFromDescription();

        app.MapGet("/", () => Results.Redirect("/scalar"))
            .RequireAuthorization(OperatorPolicy)
            .ExcludeFromDescription();

        return app;
    }

    private static bool IsControl(HttpRequest request) => request.Path.StartsWithSegments(ControlPrefix);

    private static bool IsOperator(System.Security.Claims.ClaimsPrincipal? principal) =>
        principal?.HasClaim("role", OperatorRole) == true;

    private static string EndSessionUrl(HttpContext http)
    {
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var authority = (configuration["Authentication:Authority"] ?? "").TrimEnd('/');
        return QueryHelpers.AddQueryString(authority + "/connect/logout", new Dictionary<string, string?>
        {
            ["client_id"] = Services.Simulator,
            ["post_logout_redirect_uri"] = Origin(http) + "/"
        });
    }

    private static string Origin(HttpContext http)
    {
        var configured = http.RequestServices.GetRequiredService<IConfiguration>()["Authentication:PublicOrigin"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        return $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}".TrimEnd('/');
    }
}
