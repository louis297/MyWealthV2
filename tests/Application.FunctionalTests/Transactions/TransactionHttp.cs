using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

internal static class TransactionHttp
{
    public static async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        string? accessToken,
        object? body = null,
        Guid? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.Value.ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await FunctionalTestSetup.WebClient.SendAsync(request);
    }
}
