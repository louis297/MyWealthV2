using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyWealthV2.Shared;

namespace MyWealthV2.BackOffice;

public static class BackOfficeAuthenticationExtensions
{
    public const string SessionCookieName = "__Host-back-office";

    public const string SystemAdminPolicy = "SystemAdmin";

    public static IHostApplicationBuilder AddBackOfficeAuthentication(this IHostApplicationBuilder builder)
    {
        var authority = builder.Configuration["Authentication:Authority"];
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(SystemAdminPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireClaim("role", "systemAdmin"));
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
                options.AccessDeniedPath = "/forbidden";
                options.SlidingExpiration = false;
                options.Events.OnSigningIn = context =>
                {
                    context.Properties.IsPersistent = false;
                    context.CookieOptions.Expires = null;
                    context.CookieOptions.MaxAge = null;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = Services.BackOffice;
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
            });

        return builder;
    }
}
