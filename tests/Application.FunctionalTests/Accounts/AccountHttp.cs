using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

internal static class AccountHttp
{
    public static Task<TokenResponse> SignInSystemAdmin() =>
        FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

    public static async Task<(Tenant Tenant, User Adviser, User Customer, TokenResponse Tokens)> SignInTenantAdmin(
        string tenantName = "North Advisory",
        string tenantCode = "north-advisory",
        string email = "tess@north.example",
        string password = "Password1!",
        string adviserEmail = "ann@north.example",
        string customerEmail = "ned@north.example")
    {
        var tenant = await TestApp.CreateTenantAsync(tenantName, tenantCode);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess Admin", email, password, tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann Adviser", adviserEmail, "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Ned North", customerEmail, "Password1!", tenant.Id, adviser.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(email, password, tenantCode);
        return (tenant, adviser, customer, tokens);
    }

    public static async Task<(Tenant Tenant, User Adviser, User Customer, TokenResponse Tokens)> SignInAdviser(
        string tenantName = "North Advisory",
        string tenantCode = "north-advisory",
        string email = "ann@north.example",
        string password = "Password1!",
        string customerEmail = "ned@north.example")
    {
        var tenant = await TestApp.CreateTenantAsync(tenantName, tenantCode);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann Adviser", email, password, tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Ned North", customerEmail, "Password1!", tenant.Id, adviser.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(email, password, tenantCode);
        return (tenant, adviser, customer, tokens);
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
