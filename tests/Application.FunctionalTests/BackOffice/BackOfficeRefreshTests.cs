extern alias BackOfficeHost;

using System.Net;
using System.Text;
using System.Web;
using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficeRefreshTests
{
    [Test]
    public async Task WebApi401_RefreshesOnce_AndRetriesWithTheNewAccessToken()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
        api.Enqueue(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        var tokenEndpoint = new ScriptedHandler();
        tokenEndpoint.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"next-access","refresh_token":"next-refresh","expires_in":900}""");
        var store = new MemoryAccessTokens("old-access", "old-refresh");
        var platform = new PlatformApi(
            new HttpClient(api) { BaseAddress = new Uri("http://webapi") },
            new HttpClient(tokenEndpoint) { BaseAddress = new Uri("http://identity") },
            store,
            BackOfficeTestSecret.Value);

        var body = await platform.ListTenantsAsync();

        body.ShouldContain("\"items\"");
        api.Requests.Count.ShouldBe(2);
        api.Requests[0].Authorization.ShouldBe("Bearer old-access");
        api.Requests[1].Authorization.ShouldBe("Bearer next-access");
        tokenEndpoint.Requests.Count.ShouldBe(1);
        var form = HttpUtility.ParseQueryString(tokenEndpoint.Requests[0].Body);
        form["grant_type"].ShouldBe("refresh_token");
        form["refresh_token"].ShouldBe("old-refresh");
        form["client_id"].ShouldBe(Services.BackOffice);
        form["client_secret"].ShouldBe(BackOfficeTestSecret.Value);
        store.AccessToken.ShouldBe("next-access");
        store.RefreshToken.ShouldBe("next-refresh");
    }

    private sealed class MemoryAccessTokens(string? access, string? refresh) : IAccessTokens
    {
        public string? AccessToken { get; private set; } = access;

        public string? RefreshToken { get; private set; } = refresh;

        public void Store(string accessToken, string refreshToken)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.ToString(),
                body));
            var next = _responses.Dequeue();
            return new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(string Method, string Path, string? Authorization, string Body);
}
