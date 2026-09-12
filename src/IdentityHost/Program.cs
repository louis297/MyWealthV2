using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.IdentityHost;
using MyWealthV2.IdentityHost.Services;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructureServices();

builder.Services.AddScoped<IUser, AnonymousUser>();
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SignInManager<ApplicationUser>>();

var portalOrigins = OpenIddictClientUris.ReadPortalOrigins(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (portalOrigins.Count == 0)
        {
            policy.SetIsOriginAllowed(_ => false);
            return;
        }

        policy.WithOrigins(portalOrigins.ToArray())
            .WithMethods("GET", "POST", "OPTIONS")
            .WithHeaders("Content-Type");
    });
});

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/login";
    });

builder.Services.AddOpenIddict()
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetRevocationEndpointUris("connect/revocation")
            .SetEndSessionEndpointUris("connect/logout")
            .SetUserInfoEndpointUris("connect/userinfo");

        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow()
            .RequireProofKeyForCodeExchange();

        options.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, "api");

        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(14));
        options.UseReferenceRefreshTokens();
        options.DisableSlidingRefreshTokenExpiration();
        options.DisableAccessTokenEncryption();

        options.AddDevelopmentEncryptionCertificate()
            .AddDevelopmentSigningCertificate();

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .DisableTransportSecurityRequirement();
    });

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await OpenIddictSeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapRazorPages();
app.MapControllers();

app.Run();

public partial class Program;
