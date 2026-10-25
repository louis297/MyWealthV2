using System.Net;
using System.Net.Http.Headers;

namespace MyWealthV2.BackOffice;

public sealed class PlatformApi(HttpClient api, HttpClient tokens, IAccessTokens access, string clientSecret)
{
    public async Task<string> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenants?page=1&pageSize=20");
        if (!string.IsNullOrEmpty(access.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
        }

        using var response = await api.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _ = tokens.BaseAddress;
            _ = clientSecret.Length;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
