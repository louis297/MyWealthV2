using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MyWealthV2.Shared;

namespace MyWealthV2.BackOffice;

public sealed class PlatformApi(HttpClient api, HttpClient tokens, IAccessTokens access, string clientSecret)
{
    public Task<string> ListTenantsAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "/tenants?page=1&pageSize=20", json: null, cancellationToken);

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
