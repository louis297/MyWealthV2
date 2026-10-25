using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace MyWealthV2.BackOffice;

public static class PlatformApiRegistration
{
    public static IHostApplicationBuilder AddPlatformApi(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAccessTokens, CookieAccessTokens>();
        var apiBase = builder.Configuration["Api:BaseAddress"] ?? "https+http://webapi";
        builder.Services.AddHttpClient("back-office-webapi", client => client.BaseAddress = new Uri(apiBase));
        builder.Services.AddScoped<IPlatformApi>(services =>
        {
            var api = services.GetRequiredService<IHttpClientFactory>().CreateClient("back-office-webapi");
            var configuration = services.GetRequiredService<IConfiguration>();
            var authority = configuration["Authentication:Authority"];
            var oidc = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(OpenIdConnectDefaults.AuthenticationScheme);
            var handler = oidc.BackchannelHttpHandler ?? new HttpClientHandler();
            var tokens = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = string.IsNullOrWhiteSpace(authority)
                    ? oidc.Backchannel.BaseAddress
                    : new Uri(authority.TrimEnd('/') + "/")
            };
            return new PlatformApi(
                api,
                tokens,
                services.GetRequiredService<IAccessTokens>(),
                configuration["Authentication:ClientSecret"] ?? "");
        });
        return builder;
    }
}
