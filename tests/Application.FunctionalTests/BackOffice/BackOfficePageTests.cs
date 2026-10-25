extern alias BackOfficeHost;

using System.Net;
using System.Security.Claims;
using BackOfficeHost::MyWealthV2.BackOffice;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficePageTests
{
    private const string SessionCookieName = "__Host-back-office";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PersonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ReportId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    private BackOfficeFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly StubApi _api = new();

    [OneTimeSetUp]
    public void Start()
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        _factory = new BackOfficeFactory(
            identity.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
            api: _api);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task TenantDetail_ShowsCodeAsText_AndHasNoDelete()
    {
        var body = await GetAsync($"/tenants/{TenantId}");

        body.ShouldContain("north-advisory");
        body.ShouldNotContain("name=\"code\"");
        body.ShouldNotContain(">Delete<");
    }

    [Test]
    public async Task TenantAdminDetail_ShowsEmailAsText_AndHasNoDelete()
    {
        var body = await GetAsync($"/tenant-admins/{PersonId}");

        body.ShouldContain("alex@firm");
        body.ShouldNotContain("name=\"email\"");
        body.ShouldNotContain(">Delete<");
    }

    [Test]
    public async Task Configuration_ListsEachReport_WithEnableOrDisable_AndDoesNotOfferRunOrSchedule()
    {
        var body = await GetAsync($"/tenants/{TenantId}/config");

        var customerAt = body.IndexOf("customer-transactions", StringComparison.Ordinal);
        var adviserAt = body.IndexOf("adviser-balances", StringComparison.Ordinal);
        var grantedAt = body.IndexOf(">Granted<", StringComparison.Ordinal);
        var notGrantedAt = body.IndexOf(">Not granted<", StringComparison.Ordinal);
        var disableAt = body.IndexOf(">Disable<", StringComparison.Ordinal);
        var enableAt = body.IndexOf(">Enable<", StringComparison.Ordinal);
        customerAt.ShouldBeGreaterThanOrEqualTo(0);
        adviserAt.ShouldBeGreaterThan(customerAt);
        grantedAt.ShouldBeGreaterThan(customerAt);
        grantedAt.ShouldBeLessThan(adviserAt);
        disableAt.ShouldBeGreaterThan(grantedAt);
        disableAt.ShouldBeLessThan(adviserAt);
        notGrantedAt.ShouldBeGreaterThan(adviserAt);
        enableAt.ShouldBeGreaterThan(notGrantedAt);
        body.ShouldNotContain("Report id");
        body.ShouldNotContain(">Grant<");
        body.ShouldNotContain(">Revoke<");
        body.ShouldNotContain(">Run<");
        body.ShouldNotContain(">Schedule<");
        body.ShouldNotContain("/reports/request");
        body.ShouldNotContain("/reports/schedules");
    }

    private async Task<string> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Cookie", SessionCookie());
        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private string SessionCookie()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(
            [new Claim("role", "systemAdmin"), new Claim("name", "Test")],
            CookieAuthenticationDefaults.AuthenticationScheme,
            "name",
            "role");
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties(),
            CookieAuthenticationDefaults.AuthenticationScheme);
        return SessionCookieName + "=" + options.TicketDataFormat.Protect(ticket);
    }

    private sealed class StubApi : IPlatformApi
    {
        public Task<TenantPage> ListTenantsAsync(int page, int pageSize, bool? isEnabled, string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenantPage([], page, pageSize, 0));

        public Task<TenantItem> GetTenantAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenantItem(id, "North Advisory", "north-advisory", "NZD", true, "rv1"));

        public Task<Guid> CreateTenantAsync(string name, string code, string reportingCurrency, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantId);

        public Task UpdateTenantAsync(Guid id, string name, string reportingCurrency, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DisableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<TenantAdminPage> ListTenantAdminsAsync(int page, int pageSize, Guid? tenantId, bool? enabledOnly, string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenantAdminPage([], page, pageSize, 0));

        public Task<TenantAdminItem> GetTenantAdminAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenantAdminItem(id, TenantId, "Alex", "alex@firm", "active", true, "rv1"));

        public Task<Guid> CreateTenantAdminAsync(Guid tenantId, string name, string email, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(PersonId);

        public Task UpdateTenantAdminAsync(Guid id, string name, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DisableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnableTenantAdminAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ReportGrantItem>> ListReportGrantsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReportGrantItem>>([
                new ReportGrantItem(ReportId, "customer-transactions", "Customer Transactions Report", "Customer", true, true),
                new ReportGrantItem(AdviserBalancesId, "adviser-balances", "Adviser Balances Report", "Book", false, false)
            ]);

        public Task GrantReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RevokeReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<CurrencyItem>> ListCurrenciesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CurrencyItem>>([new CurrencyItem("NZD", "New Zealand Dollar", true)]);
    }
}
