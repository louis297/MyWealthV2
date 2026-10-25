extern alias BackOfficeHost;

using System.Net;
using System.Text;
using System.Text.Json;
using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficeApiTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PersonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ReportId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task TenantCalls_UseTheExistingShapes()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        api.Enqueue(HttpStatusCode.OK, TenantJson());
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{TenantId}}"}""");
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        await platform.ListTenantsAsync(1, 20, true, "north");
        await platform.GetTenantAsync(TenantId);
        await platform.CreateTenantAsync("North", "north", "nzd");
        await platform.UpdateTenantAsync(TenantId, "North Ltd", "AUD", "rv1");
        await platform.DisableTenantAsync(TenantId, "rv1");
        await platform.EnableTenantAsync(TenantId, "rv2");

        AssertRequest(api.Requests[0], "GET", "/tenants?page=1&pageSize=20&isEnabled=true&search=north");
        AssertRequest(api.Requests[1], "GET", $"/tenants/{TenantId}");
        AssertRequest(api.Requests[2], "POST", "/tenants");
        Body(api.Requests[2]).GetProperty("name").GetString().ShouldBe("North");
        Body(api.Requests[2]).GetProperty("code").GetString().ShouldBe("north");
        Body(api.Requests[2]).GetProperty("reportingCurrency").GetString().ShouldBe("nzd");
        AssertRequest(api.Requests[3], "PUT", $"/tenants/{TenantId}");
        Body(api.Requests[3]).GetProperty("name").GetString().ShouldBe("North Ltd");
        Body(api.Requests[3]).GetProperty("reportingCurrency").GetString().ShouldBe("AUD");
        Body(api.Requests[3]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        Body(api.Requests[3]).TryGetProperty("code", out _).ShouldBeFalse();
        AssertRequest(api.Requests[4], "POST", $"/tenants/{TenantId}/disable");
        Body(api.Requests[4]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        Body(api.Requests[4]).TryGetProperty("code", out _).ShouldBeFalse();
        AssertRequest(api.Requests[5], "POST", $"/tenants/{TenantId}/enable");
        Body(api.Requests[5]).GetProperty("rowVersion").GetString().ShouldBe("rv2");
    }

    [Test]
    public async Task TenantAdminCalls_UseTheExistingShapes()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        api.Enqueue(HttpStatusCode.OK, AdminJson());
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{PersonId}}"}""");
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        await platform.ListTenantAdminsAsync(1, 20, TenantId, true, "alex");
        await platform.GetTenantAdminAsync(PersonId);
        await platform.CreateTenantAdminAsync(TenantId, "Alex", "alex@firm", "Password1!");
        await platform.UpdateTenantAdminAsync(PersonId, "Alex B", "rv1");
        await platform.DisableTenantAdminAsync(PersonId, "rv1");
        await platform.EnableTenantAdminAsync(PersonId, "rv2");

        AssertRequest(
            api.Requests[0],
            "GET",
            $"/users/tenant-admins?page=1&pageSize=20&tenantId={TenantId}&enabledOnly=true&search=alex");
        AssertRequest(api.Requests[1], "GET", $"/users/tenant-admins/{PersonId}");
        AssertRequest(api.Requests[2], "POST", "/users/tenant-admins");
        Body(api.Requests[2]).GetProperty("tenantId").GetGuid().ShouldBe(TenantId);
        Body(api.Requests[2]).GetProperty("name").GetString().ShouldBe("Alex");
        Body(api.Requests[2]).GetProperty("email").GetString().ShouldBe("alex@firm");
        Body(api.Requests[2]).GetProperty("password").GetString().ShouldBe("Password1!");
        Body(api.Requests[2]).TryGetProperty("isActive", out _).ShouldBeFalse();
        AssertRequest(api.Requests[3], "PUT", $"/users/tenant-admins/{PersonId}");
        Body(api.Requests[3]).GetProperty("name").GetString().ShouldBe("Alex B");
        Body(api.Requests[3]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        Body(api.Requests[3]).TryGetProperty("email", out _).ShouldBeFalse();
        Body(api.Requests[3]).TryGetProperty("tenantId", out _).ShouldBeFalse();
        AssertRequest(api.Requests[4], "POST", $"/users/tenant-admins/{PersonId}/disable");
        Body(api.Requests[4]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        AssertRequest(api.Requests[5], "POST", $"/users/tenant-admins/{PersonId}/enable");
        Body(api.Requests[5]).GetProperty("rowVersion").GetString().ShouldBe("rv2");
    }

    [Test]
    public async Task ReportGrantCalls_UseTheExistingShapes()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, "[]");
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{ReportId}}"}""");
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        (await platform.ListReportGrantsAsync(TenantId)).ShouldBeEmpty();
        await platform.GrantReportAsync(TenantId, ReportId);
        await platform.RevokeReportAsync(TenantId, ReportId);

        AssertRequest(api.Requests[0], "GET", $"/tenants/{TenantId}/reports");
        AssertRequest(api.Requests[1], "POST", $"/tenants/{TenantId}/reports");
        Body(api.Requests[1]).GetProperty("reportId").GetGuid().ShouldBe(ReportId);
        AssertRequest(api.Requests[2], "DELETE", $"/tenants/{TenantId}/reports/{ReportId}");
        api.Requests[2].Body.ShouldBeNullOrEmpty();
    }

    [Test]
    public async Task ReportGrantList_ReadsGranted()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, $$"""
            [{"id":"{{ReportId}}","code":"adviser-balances","name":"Adviser Balances Report","scope":"Book","isActive":true,"granted":true}]
            """);
        var platform = Create(api);

        var item = (await platform.ListReportGrantsAsync(TenantId)).ShouldHaveSingleItem();
        item.Code.ShouldBe("adviser-balances");
        item.Granted.ShouldBeTrue();
    }

    private static PlatformApi Create(ScriptedHandler api) =>
        new(
            new HttpClient(api) { BaseAddress = new Uri("http://webapi") },
            new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("http://identity") },
            new MemoryTokens(),
            BackOfficeTestSecret.Value);

    private static void AssertRequest(CapturedRequest request, string method, string path)
    {
        request.Method.ShouldBe(method);
        request.Path.ShouldBe(path);
    }

    private static JsonElement Body(CapturedRequest request)
    {
        using var document = JsonDocument.Parse(request.Body);
        return document.RootElement.Clone();
    }

    private static string TenantJson() =>
        $$"""
        {"id":"{{TenantId}}","name":"North","code":"north","reportingCurrency":"NZD","isActive":true,"rowVersion":"rv1","created":"2026-10-25T00:00:00+00:00"}
        """;

    private static string AdminJson() =>
        $$"""
        {"id":"{{PersonId}}","tenantId":"{{TenantId}}","name":"Alex","email":"alex@firm","status":"active","isActive":true,"rowVersion":"rv1","created":"2026-10-25T00:00:00+00:00"}
        """;

    private sealed class MemoryTokens : IAccessTokens
    {
        public string? AccessToken { get; private set; } = "access";

        public string? RefreshToken { get; private set; } = "refresh";

        public void Store(string accessToken, string refreshToken)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
        }
    }

    private sealed record CapturedRequest(string Method, string Path, string Body);

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
            Requests.Add(new CapturedRequest(request.Method.Method, request.RequestUri!.PathAndQuery, body));
            if (_responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            var next = _responses.Dequeue();
            return new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
