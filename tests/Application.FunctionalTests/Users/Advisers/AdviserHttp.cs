using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Users.Advisers;

internal static class AdviserHttp
{
    public static Task<TokenResponse> SignInSystemAdmin() =>
        FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

    public static async Task<(Tenant Tenant, TokenResponse Tokens)> SignInTenantAdmin(
        string tenantName = "North Advisory",
        string tenantCode = "north-advisory",
        string email = "tess@north.example",
        string password = "Password1!")
    {
        var tenant = await TestApp.CreateTenantAsync(tenantName, tenantCode);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess Admin", email, password, tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(email, password, tenantCode);
        return (tenant, tokens);
    }

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
