extern alias BackOfficeHost;

using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
    private static readonly Guid InstrumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondInstrumentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid EmptyTenantId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OutsideTenantId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MissingTenantId = Guid.Parse("77777777-7777-7777-7777-777777777777");

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
    public async Task Configuration_ListsActiveReports_HidesInactive_AndDoesNotOfferRunOrSchedule()
    {
        var body = await GetAsync($"/tenants/{TenantId}/config");

        body.ShouldContain("customer-transactions");
        body.ShouldContain(">Granted<");
        body.ShouldContain(">Disable<");
        body.ShouldNotContain("adviser-balances");
        body.ShouldNotContain(">Not granted<");
        body.ShouldNotContain(">Catalog<");
        body.ShouldNotContain("Report id");
        body.ShouldNotContain(">Grant<");
        body.ShouldNotContain(">Revoke<");
        body.ShouldNotContain(">Run<");
        body.ShouldNotContain(">Schedule<");
        body.ShouldNotContain("/reports/request");
        body.ShouldNotContain("/reports/schedules");
    }

    [Test]
    public async Task Instruments_WithoutTenant_PromptsAndDoesNotCallTheList()
    {
        var callsBefore = _api.InstrumentListCalls;

        var body = await GetAsync("/instruments");

        body.ShouldContain("Choose a tenant");
        body.ShouldNotContain("VTI");
        body.ShouldContain("href=\"/instruments\"");
        _api.InstrumentListCalls.ShouldBe(callsBefore);
    }

    [Test]
    public async Task Instruments_WithTenant_ShowsTheCatalogInServerOrder()
    {
        var listsBefore = _api.InstrumentLists.Count;
        var tenantsBefore = _api.TenantLists.Count;

        var body = await GetAsync($"/instruments?tenantId={TenantId}&enabledOnly=true&search=vti&page=2&pageSize=50");

        var call = _api.InstrumentLists[listsBefore];
        call.Page.ShouldBe(2);
        call.PageSize.ShouldBe(50);
        call.TenantId.ShouldBe(TenantId);
        call.EnabledOnly.ShouldBe(true);
        call.Search.ShouldBe("vti");
        var tenants = _api.TenantLists[tenantsBefore];
        tenants.Page.ShouldBe(1);
        tenants.PageSize.ShouldBe(100);
        tenants.IsEnabled.ShouldBe(true);
        var zuluAt = body.IndexOf("ZZZ", StringComparison.Ordinal);
        var vtiAt = body.IndexOf("VTI", StringComparison.Ordinal);
        zuluAt.ShouldBeGreaterThanOrEqualTo(0);
        vtiAt.ShouldBeGreaterThan(zuluAt);
        body.ShouldContain("Zulu");
        body.ShouldContain("Vanguard");
        body.ShouldContain("USD");
        body.ShouldContain("NZD");
        body.ShouldContain(">Active<");
        body.ShouldContain(">Disabled<");
        body.ShouldContain(">Open<");
        body.ShouldContain($"href=\"/instruments/{InstrumentId}\"");
        body.ShouldContain($"href=\"/instruments/new?tenantId={TenantId}\"");
        body.ShouldContain("name=\"tenantId\"");
        body.ShouldContain("name=\"search\"");
        body.ShouldContain("name=\"enabledOnly\"");
        body.ShouldNotContain(">Delete<");
        body.ShouldNotContain("Price");
        body.ShouldNotContain("/holdings");
        body.ShouldNotContain("/accounts");
    }

    [Test]
    public async Task Instruments_WithTenantAndNoRows_ShowsAnEmptyCatalog()
    {
        var body = await GetAsync($"/instruments?tenantId={EmptyTenantId}");

        body.ShouldContain("No instruments");
        body.ShouldNotContain("ZZZ");
        body.ShouldNotContain($"href=\"/instruments/new?tenantId={EmptyTenantId}\"");
    }

    [Test]
    public async Task Instruments_ForATenantOutsideTheActiveList_DoesNotOfferCreate()
    {
        var body = await GetAsync($"/instruments?tenantId={OutsideTenantId}");

        body.ShouldContain("Outside");
        body.ShouldNotContain($"href=\"/instruments/new?tenantId={OutsideTenantId}\"");
    }

    [Test]
    public async Task Instruments_ForAnUnknownTenant_ShowsTheApiError()
    {
        var body = await GetAsync($"/instruments?tenantId={MissingTenantId}");

        body.ShouldContain("Tenant was not found.");
        body.ShouldNotContain($"href=\"/instruments/new?tenantId={MissingTenantId}\"");
    }

    [Test]
    public async Task InstrumentNew_PrefillsAnActiveTenantAndOffersQuoteCurrency()
    {
        var body = await GetAsync($"/instruments/new?tenantId={TenantId}");

        body.ShouldContain("North Advisory");
        body.ShouldContain(TenantId.ToString());
        body.ShouldContain("Symbol");
        body.ShouldContain("Name");
        body.ShouldContain("NZD");
        body.ShouldContain(">Create<");
        body.ShouldNotContain(">Delete<");
        body.ShouldNotContain("Price");
    }

    [Test]
    public async Task InstrumentNew_ForATenantOutsideTheActiveList_DoesNotOfferCreate()
    {
        var body = await GetAsync($"/instruments/new?tenantId={OutsideTenantId}");

        body.ShouldNotContain(">Create<");
        body.ShouldNotContain(OutsideTenantId.ToString());
    }

    [Test]
    public async Task InstrumentDetail_ShowsQuoteCurrencyAndDisableWhenActive()
    {
        var body = await GetAsync($"/instruments/{InstrumentId}");

        body.ShouldContain("VTI");
        body.ShouldContain("Vanguard");
        body.ShouldContain("USD");
        body.ShouldContain("Symbol");
        body.ShouldContain("Name");
        body.ShouldContain($"href=\"/tenants/{TenantId}\"");
        body.ShouldContain(">Disable<");
        body.ShouldNotContain(">Enable<");
        body.ShouldNotContain("name=\"quoteCurrency\"");
        body.ShouldNotContain(">Delete<");
        body.ShouldNotContain("Price");
        body.ShouldNotContain("/holdings");
        body.ShouldNotContain("/accounts");
    }

    [Test]
    public async Task InstrumentDetail_ShowsEnableWhenInactive()
    {
        var body = await GetAsync($"/instruments/{SecondInstrumentId}");

        body.ShouldContain("CASH");
        body.ShouldContain(">Enable<");
        body.ShouldNotContain(">Disable<");
    }

    [Test]
    public async Task ReportsPage_ListsCatalog_IncludingInactive_AndDoesNotOfferRunOrSchedule()
    {
        var body = await GetAsync("/reports");

        var customerAt = body.IndexOf("customer-transactions", StringComparison.Ordinal);
        var adviserAt = body.IndexOf("adviser-balances", StringComparison.Ordinal);
        customerAt.ShouldBeGreaterThanOrEqualTo(0);
        adviserAt.ShouldBeGreaterThan(customerAt);
        body.ShouldContain(">Active<");
        body.ShouldContain(">Inactive<");
        body.ShouldContain("manual");
        body.ShouldContain("scheduled");
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
        return VisibleMarkup(await response.Content.ReadAsStringAsync());
    }

    // Interactive Server writes a protected marker and an antiforgery token into
    // every response. Those values are base64, so a short symbol such as ZZZ or
    // VTI can appear in them even when the catalog does not render it.
    private static string VisibleMarkup(string html) =>
        ProtectedPayload.Replace(
            html,
            match => match.Value.StartsWith("<!--", StringComparison.Ordinal)
                ? ""
                : "name=\"__RequestVerificationToken\" value=\"\"");

    private static readonly Regex ProtectedPayload = new(
        "<!--Blazor.*?-->|name=\"__RequestVerificationToken\"\\s+value=\"[^\"]*\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

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
        public List<TenantListRequest> TenantLists { get; } = [];

        public Task<TenantPage> ListTenantsAsync(int page, int pageSize, bool? isEnabled, string? search, CancellationToken cancellationToken = default)
        {
            TenantLists.Add(new TenantListRequest(page, pageSize, isEnabled, search));
            return Task.FromResult(new TenantPage(
                [new TenantItem(TenantId, "North Advisory", "north-advisory", "NZD", true, "rv1")],
                page,
                pageSize,
                1));
        }

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

        public Task<IReadOnlyList<PlatformReportItem>> ListReportCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformReportItem>>([
                new PlatformReportItem(ReportId, "customer-transactions", "Customer Transactions Report", "Customer", ["manual", "scheduled"], true),
                new PlatformReportItem(AdviserBalancesId, "adviser-balances", "Adviser Balances Report", "Book", ["manual"], false)
            ]);

        public Task UpdateReportCatalogAsync(
            Guid reportId,
            bool? isActive,
            IReadOnlyList<string>? allowedTriggers,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<CurrencyItem>> ListCurrenciesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CurrencyItem>>([new CurrencyItem("NZD", "New Zealand Dollar", true)]);

        public int InstrumentListCalls { get; private set; }

        public List<InstrumentListRequest> InstrumentLists { get; } = [];

        public Task<InstrumentPage> ListInstrumentsAsync(
            int page,
            int pageSize,
            Guid tenantId,
            bool? enabledOnly,
            string? search,
            CancellationToken cancellationToken = default)
        {
            InstrumentListCalls++;
            InstrumentLists.Add(new InstrumentListRequest(page, pageSize, tenantId, enabledOnly, search));
            if (tenantId == EmptyTenantId)
            {
                return Task.FromResult(new InstrumentPage([], page, pageSize, 0));
            }

            if (tenantId == MissingTenantId)
            {
                throw new PlatformApiException(404, "Tenant was not found.");
            }

            if (tenantId == OutsideTenantId)
            {
                return Task.FromResult(new InstrumentPage(
                    [new InstrumentItem(InstrumentId, tenantId, "OUT", "Outside", "USD", true, "rv1")],
                    page,
                    pageSize,
                    1));
            }

            return Task.FromResult(new InstrumentPage(
                [
                    new InstrumentItem(InstrumentId, tenantId, "ZZZ", "Zulu", "USD", false, "rv1"),
                    new InstrumentItem(SecondInstrumentId, tenantId, "VTI", "Vanguard", "NZD", true, "rv2")
                ],
                page,
                pageSize,
                2));
        }

        public readonly record struct TenantListRequest(int Page, int PageSize, bool? IsEnabled, string? Search);

        public readonly record struct InstrumentListRequest(
            int Page,
            int PageSize,
            Guid TenantId,
            bool? EnabledOnly,
            string? Search);

        public Task<Guid> CreateInstrumentAsync(
            Guid tenantId,
            string symbol,
            string name,
            string quoteCurrency,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        public Task<InstrumentItem> GetInstrumentAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == SecondInstrumentId)
            {
                return Task.FromResult(new InstrumentItem(id, TenantId, "CASH", "Cash", "NZD", false, "rv2"));
            }

            return Task.FromResult(new InstrumentItem(id, TenantId, "VTI", "Vanguard", "USD", true, "rv1"));
        }

        public Task UpdateInstrumentAsync(
            Guid id,
            string symbol,
            string name,
            string rowVersion,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DisableInstrumentAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnableInstrumentAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
