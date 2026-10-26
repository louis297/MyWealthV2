using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MyWealthV2.Shared;

namespace MyWealthV2.BackOffice;

public sealed class PlatformApi(HttpClient api, HttpClient tokens, IAccessTokens access, string clientSecret) : IPlatformApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<string> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        var (_, body) = await SendAsync(HttpMethod.Get, "/tenants?page=1&pageSize=20", json: null, cancellationToken);
        return body;
    }

    public async Task<TenantPage> ListTenantsAsync(
        int page,
        int pageSize,
        bool? isEnabled,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var path = Query("/tenants", new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["isEnabled"] = Flag(isEnabled),
            ["search"] = search
        });
        return await ReadAsync<TenantPage>(HttpMethod.Get, path, json: null, cancellationToken);
    }

    public Task<TenantItem> GetTenantAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReadAsync<TenantItem>(HttpMethod.Get, $"/tenants/{id}", json: null, cancellationToken);

    public async Task<Guid> CreateTenantAsync(
        string name,
        string code,
        string reportingCurrency,
        CancellationToken cancellationToken = default)
    {
        var created = await ReadAsync<Created>(
            HttpMethod.Post,
            "/tenants",
            JsonSerializer.Serialize(new { name, code, reportingCurrency }, Json),
            cancellationToken);
        return created.Id;
    }

    public Task UpdateTenantAsync(
        Guid id,
        string name,
        string reportingCurrency,
        string rowVersion,
        CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(
            HttpMethod.Put,
            $"/tenants/{id}",
            JsonSerializer.Serialize(new { name, reportingCurrency, rowVersion }, Json),
            cancellationToken);

    public Task DisableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(HttpMethod.Post, $"/tenants/{id}/disable", RowVersion(rowVersion), cancellationToken);

    public Task EnableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(HttpMethod.Post, $"/tenants/{id}/enable", RowVersion(rowVersion), cancellationToken);

    public async Task<TenantAdminPage> ListTenantAdminsAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        bool? enabledOnly,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var path = Query("/users/tenant-admins", new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["tenantId"] = tenantId?.ToString(),
            ["enabledOnly"] = Flag(enabledOnly),
            ["search"] = search
        });
        return await ReadAsync<TenantAdminPage>(HttpMethod.Get, path, json: null, cancellationToken);
    }

    public Task<TenantAdminItem> GetTenantAdminAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReadAsync<TenantAdminItem>(HttpMethod.Get, $"/users/tenant-admins/{id}", json: null, cancellationToken);

    public async Task<Guid> CreateTenantAdminAsync(
        Guid tenantId,
        string name,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var created = await ReadAsync<Created>(
            HttpMethod.Post,
            "/users/tenant-admins",
            JsonSerializer.Serialize(new { tenantId, name, email, password }, Json),
            cancellationToken);
        return created.Id;
    }

    public Task UpdateTenantAdminAsync(
        Guid id,
        string name,
        string rowVersion,
        CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(
            HttpMethod.Put,
            $"/users/tenant-admins/{id}",
            JsonSerializer.Serialize(new { name, rowVersion }, Json),
            cancellationToken);

    public Task DisableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(
            HttpMethod.Post,
            $"/users/tenant-admins/{id}/disable",
            RowVersion(rowVersion),
            cancellationToken);

    public Task EnableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(
            HttpMethod.Post,
            $"/users/tenant-admins/{id}/enable",
            RowVersion(rowVersion),
            cancellationToken);

    public Task<IReadOnlyList<ReportGrantItem>> ListReportGrantsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<ReportGrantItem>>(
            HttpMethod.Get,
            $"/tenants/{tenantId}/reports",
            json: null,
            cancellationToken);

    public Task GrantReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(
            HttpMethod.Post,
            $"/tenants/{tenantId}/reports",
            JsonSerializer.Serialize(new { reportId }, Json),
            cancellationToken);

    public Task RevokeReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default) =>
        SendExpectingSuccessAsync(
            HttpMethod.Delete,
            $"/tenants/{tenantId}/reports/{reportId}",
            json: null,
            cancellationToken);

    public Task<IReadOnlyList<PlatformReportItem>> ListReportCatalogAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PlatformReportItem>>(
            HttpMethod.Get,
            "/reports/catalog",
            json: null,
            cancellationToken);

    public Task UpdateReportCatalogAsync(
        Guid reportId,
        bool? isActive,
        IReadOnlyList<string>? allowedTriggers,
        CancellationToken cancellationToken = default)
    {
        object body = (isActive, allowedTriggers) switch
        {
            (not null, not null) => new { isActive, allowedTriggers },
            (not null, null) => new { isActive },
            (null, not null) => new { allowedTriggers },
            _ => throw new ArgumentException("isActive or allowedTriggers is required.")
        };
        return SendExpectingSuccessAsync(
            HttpMethod.Put,
            $"/reports/{reportId}",
            JsonSerializer.Serialize(body, Json),
            cancellationToken);
    }

    public Task<IReadOnlyList<CurrencyItem>> ListCurrenciesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<CurrencyItem>>(
            HttpMethod.Get,
            "/currencies?enabledOnly=true",
            json: null,
            cancellationToken);

    private static string RowVersion(string rowVersion) =>
        JsonSerializer.Serialize(new { rowVersion }, Json);

    private static string? Flag(bool? value) => value switch
    {
        true => "true",
        false => "false",
        _ => null
    };

    private static string Query(string path, IReadOnlyDictionary<string, string?> values)
    {
        var pairs = values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!));
        var query = string.Join("&", pairs);
        return query.Length == 0 ? path : path + "?" + query;
    }

    private async Task<T> ReadAsync<T>(HttpMethod method, string path, string? json, CancellationToken cancellationToken)
    {
        var (status, body) = await SendExpectingBodyAsync(method, path, json, cancellationToken);
        if (status is < 200 or >= 300)
        {
            throw new PlatformApiException(status, body);
        }

        return JsonSerializer.Deserialize<T>(body, Json)
            ?? throw new PlatformApiException(status, body);
    }

    private async Task SendExpectingSuccessAsync(
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        var (status, body) = await SendExpectingBodyAsync(method, path, json, cancellationToken);
        if (status is < 200 or >= 300)
        {
            throw new PlatformApiException(status, body);
        }
    }

    private Task<(int Status, string Body)> SendExpectingBodyAsync(
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken) =>
        SendAsync(method, path, json, cancellationToken);

    private sealed record Created(Guid Id);

    private async Task<(int Status, string Body)> SendAsync(
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        using var response = await SendOnceAsync(method, path, json, access.AccessToken, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        var next = await RefreshAsync(cancellationToken);
        if (next is null)
        {
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        using var retry = await SendOnceAsync(method, path, json, next, cancellationToken);
        return ((int)retry.StatusCode, await retry.Content.ReadAsStringAsync(cancellationToken));
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
