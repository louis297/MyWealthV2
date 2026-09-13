using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

internal static class TenantHttp
{
    public static Task<TokenResponse> SignInSystemAdmin() =>
        FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

    public static async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        string? accessToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await FunctionalTestSetup.WebClient.SendAsync(request);
    }
}
