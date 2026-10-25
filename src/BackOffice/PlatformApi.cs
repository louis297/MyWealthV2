using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MyWealthV2.Shared;

namespace MyWealthV2.BackOffice;

public sealed class PlatformApi(HttpClient api, HttpClient tokens, IAccessTokens access, string clientSecret) : IPlatformApi
{
    public Task<string> ListTenantsAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "/tenants?page=1&pageSize=20", json: null, cancellationToken);

    public Task<TenantPage> ListTenantsAsync(int page, int pageSize, bool? isEnabled, string? search, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<TenantItem> GetTenantAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<Guid> CreateTenantAsync(string name, string code, string reportingCurrency, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task UpdateTenantAsync(Guid id, string name, string reportingCurrency, string rowVersion, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DisableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task EnableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<TenantAdminPage> ListTenantAdminsAsync(int page, int pageSize, Guid? tenantId, bool? enabledOnly, string? search, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<TenantAdminItem> GetTenantAdminAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<Guid> CreateTenantAdminAsync(Guid tenantId, string name, string email, string password, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task UpdateTenantAdminAsync(Guid id, string name, string rowVersion, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DisableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task EnableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<ReportGrantItem>> ListReportGrantsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task GrantReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task RevokeReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<CurrencyItem>> ListCurrenciesAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        using var response = await SendOnceAsync(method, path, json, access.AccessToken, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        var next = await RefreshAsync(cancellationToken);
        if (next is null)
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        using var retry = await SendOnceAsync(method, path, json, next, cancellationToken);
        return await retry.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string path,
        string? json,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await api.SendAsync(request, cancellationToken);
    }

    private async Task<string?> RefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(access.RefreshToken))
        {
            return null;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = access.RefreshToken,
                ["client_id"] = Services.BackOffice,
                ["client_secret"] = clientSecret
            })
        };
        using var response = await tokens.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var nextAccess = document.RootElement.GetProperty("access_token").GetString();
        var nextRefresh = document.RootElement.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString()
            : access.RefreshToken;
        if (string.IsNullOrEmpty(nextAccess) || string.IsNullOrEmpty(nextRefresh))
        {
            return null;
        }

        access.Store(nextAccess, nextRefresh);
        return nextAccess;
    }
}
