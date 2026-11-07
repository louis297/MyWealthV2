using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace MyWealthV2.BackOffice;

public static class PlatformApiRegistration
{
    public static IHostApplicationBuilder AddPlatformApi(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAccessTokens, CookieAccessTokens>();
        builder.Services.AddSingleton(services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var oidcMonitor = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
            var client = new HttpClient(new OidcBackchannelHandler(oidcMonitor), disposeHandler: true);
            return new BackOfficeTokenRefresh(
                client,
                () =>
                {
                    var oidc = oidcMonitor.Get(OpenIdConnectDefaults.AuthenticationScheme);
                    var authority = (configuration["Authentication:Authority"] ?? oidc.Authority ?? "").TrimEnd('/');
                    return authority + "/connect/token";
                },
                configuration["Authentication:ClientSecret"] ?? "",
                TimeProvider.System);
        });
        var apiBase = builder.Configuration["Api:BaseAddress"] ?? "https+http://webapi";
        builder.Services.AddHttpClient("back-office-webapi", client => client.BaseAddress = new Uri(apiBase));
        builder.Services.AddScoped<IPlatformApi>(services =>
        {
            var api = services.GetRequiredService<IHttpClientFactory>().CreateClient("back-office-webapi");
            return new PlatformApi(
                api,
                services.GetRequiredService<IAccessTokens>(),
                services.GetRequiredService<BackOfficeTokenRefresh>());
        });
        return builder;
    }

    private sealed class OidcBackchannelHandler(IOptionsMonitor<OpenIdConnectOptions> oidc) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var backchannel = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme).Backchannel;
            using var outgoing = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                var content = new ByteArrayContent(body);
                foreach (var header in request.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                outgoing.Content = content;
            }

            return await backchannel.SendAsync(outgoing, cancellationToken);
        }
    }
}
