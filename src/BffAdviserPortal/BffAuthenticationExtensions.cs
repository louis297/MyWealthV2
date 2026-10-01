using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyWealthV2.Shared;

namespace MyWealthV2.BffAdviserPortal;

public static class BffAuthenticationExtensions
{
    public const string SessionCookieName = "__Host-bff-adviser-portal";

    public static IHostApplicationBuilder AddBffAuthentication(this IHostApplicationBuilder builder)
    {
        var authority = builder.Configuration["Authentication:Authority"];

        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<AuthorizationCodeGate>();
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.Path = "/";
                options.Cookie.Domain = null;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.MaxAge = null;
                options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.SlidingExpiration = false;
                options.Events.OnSigningIn = context =>
                {
                    context.Properties.IsPersistent = false;
                    context.CookieOptions.Expires = null;
                    context.CookieOptions.MaxAge = null;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = Services.AdviserPortal;
                options.ClientSecret = builder.Configuration["Authentication:ClientSecret"];
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.SaveTokens = true;
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
                options.Scope.Add(OpenIdConnectScope.OfflineAccess);
                options.Scope.Add("api");
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
                options.Events = new OpenIdConnectEvents
                {
                    OnAuthorizationCodeReceived = OnAuthorizationCodeReceivedAsync,
                    OnTokenResponseReceived = context =>
                    {
                        CompleteOwner(context.HttpContext, context.TokenEndpointResponse);
                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = context =>
                    {
                        CompleteOwner(context.HttpContext, message: null);
                        return Task.CompletedTask;
                    }
                };
            });

        return builder;
    }

    private static async Task OnAuthorizationCodeReceivedAsync(AuthorizationCodeReceivedContext context)
    {
        var code = context.ProtocolMessage.Code;
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        var gate = context.HttpContext.RequestServices.GetRequiredService<AuthorizationCodeGate>();
        var lease = await gate.EnterAsync(code, context.HttpContext.RequestAborted);
        if (lease.IsOwner)
        {
            context.HttpContext.Items[AuthorizationCodeGate.OwnerItem] = code;
            return;
        }

        var snapshot = lease.Snapshot;
        if (snapshot is null || string.IsNullOrEmpty(snapshot.Value.IdToken))
        {
            context.Fail("The authorization code was already redeemed.");
            return;
        }

        context.HandleCodeRedemption(new OpenIdConnectMessage
        {
            AccessToken = snapshot.Value.AccessToken,
            IdToken = snapshot.Value.IdToken,
            RefreshToken = snapshot.Value.RefreshToken,
            TokenType = "Bearer"
        });
    }

    private static void CompleteOwner(HttpContext http, OpenIdConnectMessage? message)
    {
        if (http.Items[AuthorizationCodeGate.OwnerItem] is not string code)
        {
            return;
        }

        http.Items.Remove(AuthorizationCodeGate.OwnerItem);
        var snapshot = message is null
            ? (AuthorizationCodeGate.Snapshot?)null
            : new AuthorizationCodeGate.Snapshot(message.AccessToken, message.IdToken, message.RefreshToken);
        http.RequestServices.GetRequiredService<AuthorizationCodeGate>().Complete(code, snapshot);
    }
}
