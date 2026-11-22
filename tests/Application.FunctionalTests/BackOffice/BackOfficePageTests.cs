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
    private static readonly Guid MarketTenantId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid MarketInstrumentId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ClientMoneyUsdId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ClientMoneyAudId = Guid.Parse("88888888-8888-8888-8888-8888888888aa");
    private static readonly Guid ClientMoneyNzdId = Guid.Parse("99999999-9999-9999-9999-999999999999");

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
    public async Task TenantNew_OffersBothCustodyModesCheckedOnOpen_AndCreateIsEnabled()
    {
        var body = await GetAsync("/tenants/new");

        body.ShouldContain("Custody modes");
        body.ShouldContain("External accounts");
        body.ShouldContain("Custodial accounts");
        CustodyBox(body, "External").ShouldContain(" checked");
        CustodyBox(body, "Custodial").ShouldContain(" checked");
        SubmitButton(body, "Create").ShouldNotContain(" disabled");
    }

    [Test]
    public async Task TenantDetail_SetsTheCustodyModeBoxesFromTheItem_AndSaysItAffectsOnlyNewAccounts()
    {
        var body = await GetAsync($"/tenants/{TenantId}");

        body.ShouldContain("Allowed custody modes");
        CustodyBox(body, "External").ShouldContain(" checked");
        CustodyBox(body, "Custodial").ShouldNotContain(" checked");
        body.ShouldContain("Changing this affects only new accounts.");
        SubmitButton(body, "Save").ShouldNotContain(" disabled");
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
    public async Task TenantAdminDetail_ShowsTheTenantNameLinkedToTheTenant()
    {
        var body = await GetAsync($"/tenant-admins/{PersonId}");

        body.ShouldContain("North Advisory");
        body.ShouldContain($"href=\"/tenants/{TenantId}\"");
        body.ShouldNotContain($">{TenantId}<");
    }

    [Test]
    public async Task TenantAdmins_FilterPicksAnyTenantByName()
    {
        var tenantsBefore = _api.TenantLists.Count;
        var listsBefore = _api.TenantAdminLists.Count;

        var body = await GetAsync($"/tenant-admins?tenantId={TenantId}");

        body.ShouldContain("value=\"North Advisory\"");
        body.ShouldContain("mud-select");
        body.ShouldNotContain("<select");
        body.ShouldNotContain("method=\"get\"");
        body.ShouldNotContain($"value=\"{TenantId}\"");
        _api.TenantLists.Count.ShouldBe(tenantsBefore + 1);
        _api.TenantLists[tenantsBefore].IsEnabled.ShouldBeNull();
        _api.TenantAdminLists[listsBefore].ShouldBe(TenantId);
    }

    [Test]
    public async Task TenantAdmins_WithoutTenant_ListsEveryTenant()
    {
        var listsBefore = _api.TenantAdminLists.Count;

        var body = await GetAsync("/tenant-admins?enabledOnly=true");

        body.ShouldContain("value=\"All tenants\"");
        body.ShouldContain("value=\"Active\"");
        _api.TenantAdminLists[listsBefore].ShouldBeNull();
    }

    [Test]
    public async Task Tenants_FilterUsesMudBlazorFieldsAndPassesTheQuery()
    {
        var tenantsBefore = _api.TenantLists.Count;

        var body = await GetAsync("/tenants?isEnabled=false&search=north");

        body.ShouldContain("mud-select");
        body.ShouldNotContain("<select");
        body.ShouldNotContain("method=\"get\"");
        body.ShouldContain("value=\"north\"");
        body.ShouldContain("value=\"Disabled\"");
        var call = _api.TenantLists[tenantsBefore];
        call.IsEnabled.ShouldBe(false);
        call.Search.ShouldBe("north");
    }

    [Test]
    public async Task TenantAdminNew_PicksAnEnabledTenantByName()
    {
        var tenantsBefore = _api.TenantLists.Count;

        var body = await GetAsync($"/tenant-admins/new?tenantId={TenantId}");

        body.ShouldContain("North Advisory");
        body.ShouldContain("mud-select");
        body.ShouldNotContain("<option");
        body.ShouldNotContain($"value=\"{TenantId}\"");
        body.ShouldContain(">Create<");
        _api.TenantLists.Count.ShouldBe(tenantsBefore + 1);
        _api.TenantLists[tenantsBefore].IsEnabled.ShouldBe(true);
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
        // The reports part of the page: the interest run form below it has its own Run button (interest-run BO21).
        var reports = body[..body.IndexOf("Client money accounts", StringComparison.Ordinal)];
        reports.ShouldNotContain(">Run<");
        body.ShouldNotContain(">Schedule<");
        body.ShouldNotContain("/reports/request");
        body.ShouldNotContain("/reports/schedules");
    }

    [Test]
    public async Task Configuration_ListsClientMoneyAccounts_WithDisableOrEnablePerRow_AndTheAddForm()
    {
        var body = await GetAsync($"/tenants/{TenantId}/config");

        body.ShouldContain("Client money accounts");
        foreach (var header in new[] { "Currency", "Bank", "Account name", "Account number", "Active" })
        {
            body.ShouldContain($"<th>{header}</th>");
        }

        body.ShouldContain("Simulator Bank");
        body.ShouldContain("North Advisory Ltd Client Funds Trust Account");
        body.ShouldContain("02-0100-0123456-00");
        body.ShouldContain("38-9000-7654321-00");
        ClientMoneyButton(body, ClientMoneyUsdId).ShouldContain(">Disable<");
        ClientMoneyButton(body, ClientMoneyNzdId).ShouldContain(">Enable<");
        body.ShouldContain("Add client money account");
        body.ShouldContain("Bank name");
        SubmitButton(body, "Add").ShouldNotContain(" disabled");
        _api.ClientMoneyLists.ShouldContain(TenantId);
    }

    [Test]
    public async Task Configuration_ShowsTheConnectionAndTheStatementOfEachClientMoneyAccount()
    {
        var body = await GetAsync($"/tenants/{TenantId}/config");

        body.ShouldContain("<th>Connection</th>");
        body.ShouldContain("<th>Statement</th>");
        body.ShouldContain("other-bank");
        body.ShouldContain("Read 2026-11-20 02:01 UTC");
        body.ShouldContain("Not read yet");
        body.ShouldContain("Failing since 2026-11-20 03:05 UTC: Bank does not know this account");
        body.ShouldNotContain("Bank unavailable");
    }

    [TestCase("Unreachable", "Bank not reachable")]
    [TestCase("Timeout", "Bank did not answer in time")]
    [TestCase("Unauthorized", "Bank refused the key")]
    [TestCase("Unavailable", "Bank unavailable")]
    [TestCase("UnknownAccount", "Bank does not know this account")]
    [TestCase("BadResponse", "Bank answer not understood")]
    [TestCase("InvalidLine", "Statement line rejected")]
    public void FeedStatus_LabelsEveryFailure(string failure, string label)
    {
        var at = new DateTimeOffset(2026, 11, 20, 3, 5, 0, TimeSpan.Zero);

        BackOfficeHost::MyWealthV2.BackOffice.Components.Pages.FeedStatus.Text(new ClientMoneyAccountFeed(null, at, failure))
            .ShouldBe($"Failing since 2026-11-20 03:05 UTC: {label}");
        BackOfficeHost::MyWealthV2.BackOffice.Components.Pages.FeedStatus.Text(null).ShouldBe("Not read yet");
        BackOfficeHost::MyWealthV2.BackOffice.Components.Pages.FeedStatus.Text(new ClientMoneyAccountFeed(at, at.AddMinutes(-1), failure))
            .ShouldBe("Read 2026-11-20 03:05 UTC");
    }

    [Test]
    public async Task AddForm_HasABankConnectionSelect_PreselectedWithTheOnlyConnection()
    {
        var body = await GetAsync($"/tenants/{TenantId}/config");

        body.ShouldContain("Bank connection");
        Regex.IsMatch(body, "<input[^>]*value=\"simulator\"").ShouldBeTrue("the only connection is preselected");
        SubmitButton(body, "Add").ShouldNotContain(" disabled");
    }

    [Test]
    public async Task AddForm_WithNoConnection_SaysSo_AndCannotBeSubmitted()
    {
        _api.BankConnections = [];
        try
        {
            var body = await GetAsync($"/tenants/{TenantId}/config");

            body.ShouldContain("No bank connection is configured.");
            SubmitButton(body, "Add").ShouldContain(" disabled");
        }
        finally
        {
            _api.BankConnections = [new("simulator", "Simulator")];
        }
    }

    [Test]
    public async Task Configuration_OffersNoEditForClientMoneyAccounts_AndSaysHowToCorrectThem()
    {
        var body = await GetAsync($"/tenants/{TenantId}/config");

        body.ShouldContain(
            "Details cannot be changed. To correct them, disable the account and add a new one. "
            + "Disabling the last account in a currency stops new Custodial accounts in that currency.");
        body.ShouldNotContain(">Edit<");
        body.ShouldNotContain(">Save<");
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

    /// <summary>Simulated instruments BO22: the connection that prices an instrument, under its symbol.</summary>
    [Test]
    public async Task Instruments_ShowWhichMarketDataConnectionPricesAnInstrument()
    {
        var list = await GetAsync($"/instruments?tenantId={MarketTenantId}");
        list.ShouldContain("Prices from simulator");
        list.IndexOf("Prices from", StringComparison.Ordinal)
            .ShouldBeGreaterThan(list.IndexOf("ACME.SIM", StringComparison.Ordinal));
        list.IndexOf("Prices from", StringComparison.Ordinal)
            .ShouldBeLessThan(list.IndexOf("VTI", StringComparison.Ordinal));

        (await GetAsync($"/instruments/{MarketInstrumentId}")).ShouldContain("Prices from simulator");
        (await GetAsync($"/instruments/{InstrumentId}")).ShouldNotContain("Prices from");
    }

    [Test]
    public async Task NewInstrument_NotesThatAMarketDataSuffixDecidesThePriceSource()
    {
        var body = await GetAsync($"/instruments/new?tenantId={TenantId}");

        body.ShouldContain("A symbol that ends with a market data suffix (for example .SIM) is priced by that connection.");
    }

    /// <summary>Orders BO26: the tenant forms offer the broker connection, and the tenant page shows it.</summary>
    [Test]
    public async Task TenantPages_OfferTheBrokerConnection_AndTheTenantPageShowsIt()
    {
        var create = await GetAsync("/tenants/new");
        var detail = await GetAsync($"/tenants/{TenantId}");

        create.ShouldContain("Broker connection");
        detail.ShouldContain("Broker connection");
        Regex.IsMatch(detail, "Broker connection</[^>]+>\\s*<[^>]+>simulator<").ShouldBeTrue("the tenant page shows its broker connection");
    }

    /// <summary>Orders BO27: the broker connections and the state of their report feed.</summary>
    [Test]
    public async Task Broker_ListsTheConnections_WithTheStateOfTheirReports_AndIsInTheMenu()
    {
        var at = new DateTimeOffset(2026, 11, 23, 2, 13, 0, TimeSpan.Zero);
        _api.BrokerConnections =
        [
            new("alpha", "Simulator", null),
            new("beta", "Simulator", new BrokerFeedState(at, null, null)),
            new("gamma", "Simulator", new BrokerFeedState(at, at.AddMinutes(5), "InvalidItem")),
            new("delta", "Simulator", new BrokerFeedState(null, at, "Unreachable"))
        ];

        try
        {
            var body = await GetAsync("/broker");

            body.ShouldContain("href=\"/broker\"");
            body.ShouldContain(">alpha<");
            body.ShouldContain(">Simulator<");
            body.ShouldContain("Not read yet");
            body.ShouldContain("Read 2026-11-23 02:13 UTC");
            body.ShouldContain("Failing since 2026-11-23 02:18 UTC: The party sent a record that cannot be read");
            body.ShouldContain("Failing since 2026-11-23 02:13 UTC: Broker not reachable");
            body.ShouldNotContain("No broker connection is configured.");

            _api.BrokerConnections = [];
            (await GetAsync("/broker")).ShouldContain("No broker connection is configured.");
        }
        finally
        {
            _api.BrokerConnections = [new("simulator", "Simulator", null)];
        }
    }

    /// <summary>Orders BO28: the newest twenty execution reports, with a status filter; a report skipped as UnknownOrder,
    /// UnknownFill or Overfill is shown in red.</summary>
    [Test]
    public async Task Broker_ListsTheNewestReports_WithTheirLabels_FiltersByStatus_AndMarksTheOnesThatNeedAPerson()
    {
        var at = new DateTimeOffset(2026, 11, 23, 2, 13, 0, TimeSpan.Zero);
        _api.ExecutionReports =
        [
            Report("w-X5", "Fill", "Skipped", "Overfill", at.AddMinutes(4)),
            Report("w-X4", "Settled", "Skipped", "UnknownFill", at.AddMinutes(3)),
            Report("w-X3", "Cancelled", "Skipped", "UnknownOrder", at.AddMinutes(2)),
            Report("w-X2", null, "Skipped", "UnsupportedKind", at.AddMinutes(1)),
            Report("w-X1", "Fill", "Processed", null, at, quantity: 4m, price: 100.12m),
            Report("w-X0", "Fill", "Skipped", "Repeated", at.AddMinutes(-1))
        ];
        var before = _api.ExecutionReportStatuses.Count;

        try
        {
            var body = await GetAsync("/broker");

            body.ShouldContain("Execution reports");
            body.ShouldContain("2026-11-23 02:13 UTC");
            body.ShouldContain(">Unknown<");
            body.ShouldContain("Kind not supported");
            body.ShouldContain("No such order");
            body.ShouldContain("No such fill");
            body.ShouldContain("More than the order");
            body.ShouldContain("Already known");
            body.ShouldContain(">100.12<");
            body.ShouldContain(">4<");
            body.ShouldContain(">Processed<");
            body.ShouldContain("2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11");
            Regex.Matches(body, "report-needs-person").Count.ShouldBe(3);
            _api.ExecutionReportStatuses.Skip(before).ShouldBe([null]);

            var skipped = await GetAsync("/broker?status=Skipped");
            skipped.ShouldContain("href=\"/broker?status=Processed\"");
            // The processed fill is not in the filtered list.
            skipped.ShouldNotContain(">100.12<");
            _api.ExecutionReportStatuses.Last().ShouldBe("Skipped");

            await GetAsync("/broker?status=Broken");
            _api.ExecutionReportStatuses.Last().ShouldBeNull();
        }
        finally
        {
            _api.ExecutionReports = [];
        }
    }

    private static ExecutionReportItem Report(
        string outsideId,
        string? kind,
        string status,
        string? skipReason,
        DateTimeOffset receivedAt,
        decimal? quantity = null,
        decimal? price = null) =>
        new(Guid.NewGuid(), "simulator", outsideId, kind, "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11", "w-O1", quantity, price, null, null,
            null, null, null, null, receivedAt, status, skipReason, status == "Received" ? null : receivedAt.AddSeconds(1));

    /// <summary>Simulated instruments BO23: the market data connections and the state of their price feed.</summary>
    [Test]
    public async Task MarketData_ListsTheConnections_WithTheStateOfTheirPrices()
    {
        var at = new DateTimeOffset(2026, 11, 22, 3, 15, 0, TimeSpan.Zero);
        _api.MarketDataConnections =
        [
            new("alpha", "Simulator", ".ALP", null, null),
            new("beta", "Simulator", ".BET", new MarketDataFeed(at, null, null), null),
            new("gamma", "Simulator", ".GAM", new MarketDataFeed(at, at.AddMinutes(5), "Unavailable"), null)
        ];

        try
        {
            var body = await GetAsync("/market-data");

            body.ShouldContain("Market data");
            body.ShouldContain(".ALP");
            body.ShouldContain(">Simulator<");
            body.ShouldContain("Not read yet");
            body.ShouldContain("Read 2026-11-22 03:15 UTC");
            body.ShouldContain("Failing since 2026-11-22 03:20 UTC: Market data source unavailable");
            body.ShouldNotContain("No market data connection is configured.");
        }
        finally
        {
            _api.MarketDataConnections = [];
        }
    }

    /// <summary>Corporate actions BO24: the state of each connection's corporate action feed.</summary>
    [Test]
    public async Task MarketData_ShowsTheCorporateActionFeedOfEachConnection()
    {
        var at = new DateTimeOffset(2026, 11, 22, 3, 15, 0, TimeSpan.Zero);
        _api.MarketDataConnections =
        [
            new("alpha", "Simulator", ".ALP", null, null),
            new("beta", "Simulator", ".BET", new MarketDataFeed(at, null, null), new MarketDataFeed(at, at.AddMinutes(5), "InvalidItem"))
        ];

        try
        {
            var body = await GetAsync("/market-data");

            body.ShouldContain(">Corporate actions<");
            body.ShouldContain("Failing since 2026-11-22 03:20 UTC: The party sent a record that cannot be read");
        }
        finally
        {
            _api.MarketDataConnections = [];
        }
    }

    /// <summary>Corporate actions BO25: the newest twenty announcements, with a status filter; an announcement skipped as
    /// AlreadyEffective is shown in red.</summary>
    [Test]
    public async Task MarketData_ListsTheNewestAnnouncements_WithTheirLabels_AndFiltersByStatus()
    {
        var at = new DateTimeOffset(2026, 11, 22, 3, 15, 0, TimeSpan.Zero);
        _api.MarketDataConnections = [new("simulator", "Simulator", ".SIM", null, null)];
        _api.Announcements =
        [
            Announcement("w-CA2-V2", null, "Skipped", "UnsupportedKind", at.AddMinutes(2)),
            Announcement("w-CA1-V3", "Split", "Skipped", "AlreadyEffective", at.AddMinutes(1), partyStatus: "Cancelled"),
            Announcement("w-CA1-V1", "Bonus", "Processed", null, at, ratioNew: 1, ratioHeld: 5)
        ];
        var before = _api.AnnouncementStatuses.Count;

        try
        {
            var body = await GetAsync("/market-data");

            body.ShouldContain("Announcements");
            body.ShouldContain("2026-11-22 03:17 UTC");
            body.ShouldContain("Unknown");
            body.ShouldContain("Kind not supported");
            body.ShouldContain("Already applied");
            body.ShouldContain("1 for 5");
            body.ShouldContain(">Cancelled<");
            body.ShouldContain(">Processed<");
            body.ShouldContain("ACME.SIM");
            body.ShouldContain("announcement-needs-person");
            _api.AnnouncementStatuses.Skip(before).ShouldBe([null]);

            var skipped = await GetAsync("/market-data?status=Skipped");
            skipped.ShouldContain("href=\"/market-data?status=Processed\"");
            _api.AnnouncementStatuses.Last().ShouldBe("Skipped");
        }
        finally
        {
            _api.MarketDataConnections = [];
            _api.Announcements = [];
        }
    }

    [TestCase("UnsupportedKind", "Kind not supported")]
    [TestCase("Invalid", "Not valid")]
    [TestCase("NoInstrument", "No firm lists this instrument")]
    [TestCase("AlreadyEffective", "Already applied")]
    [TestCase("Superseded", "An older version")]
    public void AnnouncementReason_LabelsEveryReason(string reason, string label) =>
        BackOfficeHost::MyWealthV2.BackOffice.Components.Pages.AnnouncementText.Reason(reason).ShouldBe(label);

    private static CorporateActionAnnouncementItem Announcement(
        string outsideId,
        string? kind,
        string status,
        string? skipReason,
        DateTimeOffset receivedAt,
        string partyStatus = "Announced",
        int ratioNew = 2,
        int ratioHeld = 1) =>
        new(Guid.NewGuid(), "simulator", outsideId, outsideId[..5], 1, partyStatus, "ACME.SIM", "USD", kind, ratioNew, ratioHeld,
            new DateOnly(2026, 11, 24), null, null, receivedAt, status, skipReason, status == "Received" ? null : receivedAt);

    [Test]
    public async Task MarketData_WithNoConnection_SaysEveryInstrumentIsPricedByEodhd()
    {
        var body = await GetAsync("/market-data");

        body.ShouldContain("No market data connection is configured. Every instrument is priced by EODHD.");
    }

    [Test]
    public async Task Navigation_LinksToMarketData()
    {
        (await GetAsync("/")).ShouldContain("href=\"/market-data\"");
    }

    [TestCase("Unreachable", "Market data source not reachable")]
    [TestCase("Timeout", "Market data source did not answer in time")]
    [TestCase("Unauthorized", "Market data source refused the key")]
    [TestCase("Unavailable", "Market data source unavailable")]
    [TestCase("BadResponse", "Market data answer not understood")]
    [TestCase("InvalidItem", "The party sent a record that cannot be read")]
    public void MarketDataStatus_LabelsEveryFailure(string failure, string label)
    {
        var at = new DateTimeOffset(2026, 11, 22, 3, 15, 0, TimeSpan.Zero);

        BackOfficeHost::MyWealthV2.BackOffice.Components.Pages.MarketDataStatus.Text(new MarketDataFeed(null, at, failure))
            .ShouldBe($"Failing since 2026-11-22 03:15 UTC: {label}");
        BackOfficeHost::MyWealthV2.BackOffice.Components.Pages.MarketDataStatus.Text(null).ShouldBe("Not read yet");
        BackOfficeHost::MyWealthV2.BackOffice.Components.Pages.MarketDataStatus.Text(new MarketDataFeed(at, at.AddMinutes(-1), failure))
            .ShouldBe("Read 2026-11-22 03:15 UTC");
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
        body.ShouldContain("mud-select");
        body.ShouldNotContain("<select");
        body.ShouldNotContain("method=\"get\"");
        body.ShouldContain("value=\"North Advisory\"");
        body.ShouldContain("value=\"vti\"");
        body.ShouldContain("value=\"Active\"");
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
        body.ShouldContain("mud-select");
        body.ShouldNotContain("<option");
        body.ShouldContain($"href=\"/instruments?tenantId={TenantId}\"");
        body.ShouldContain("Symbol");
        body.ShouldContain("Name");
        body.ShouldContain("NZD");
        body.ShouldContain(">Create<");
        body.ShouldNotContain(">Delete<");
        // BO-I6: no price field. The BO22 note under the symbol says "priced" (simulated-instruments).
        body.ShouldNotContain(">Price<");
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

    private static string CustodyBox(string body, string mode)
    {
        var match = Regex.Match(body, $"<input[^>]*data-custody-mode=\"{mode}\"[^>]*>");
        match.Success.ShouldBeTrue($"no {mode} custody mode box");
        return match.Value;
    }

    [Test]
    public async Task Configuration_ShowsTheNewestInterestRuns_AndTheRunForADateForm()
    {
        var body = await GetAsync($"/tenants/{TenantId}/config");

        body.ShouldContain("Interest runs");
        foreach (var header in new[] { "As of", "Trigger", "Accounts", "Failed", "Posted", "Interest paid", "Tax withheld" })
        {
            body.ShouldContain($"<th>{header}</th>");
        }

        body.ShouldContain("Daily run");
        body.ShouldContain("Run by the platform team");
        body.ShouldContain("410.55 USD");
        body.ShouldContain("93.40 USD");
        body.ShouldContain("Run for a date");
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        Regex.IsMatch(body, $"<input[^>]*value=\"{yesterday}\"").ShouldBeTrue("the as-of date defaults to yesterday (UTC)");
        SubmitButton(body, "Run").ShouldNotContain(" disabled");
        body.ShouldContain("Runs every day by itself. Use this after a missed run. A run for a day already done changes nothing.");
        _api.InterestRunLists.ShouldContain(TenantId);
    }

    /// <summary>The Disable or Enable button of one client money account row (marked with its id).</summary>
    private static string ClientMoneyButton(string body, Guid id)
    {
        var match = Regex.Match(
            body, $"<button[^>]*data-client-money-account=\"{id}\"[^>]*>(?:(?!</button>).)*</button>", RegexOptions.Singleline);
        match.Success.ShouldBeTrue($"no button for client money account {id}");
        return match.Value;
    }

    private static string SubmitButton(string body, string label)
    {
        var match = Regex.Match(body, $"<button[^>]*type=\"submit\"[^>]*><span class=\"mud-button-label\">{label}<");
        match.Success.ShouldBeTrue($"no {label} button");
        return match.Value;
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
                [new TenantItem(TenantId, "North Advisory", "north-advisory", "NZD", ["External", "Custodial"], true, "rv1")],
                page,
                pageSize,
                1));
        }

        public Task<TenantItem> GetTenantAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenantItem(id, "North Advisory", "north-advisory", "NZD", ["External"], true, "rv1", "simulator"));

        public Task<Guid> CreateTenantAsync(string name, string code, string reportingCurrency, IReadOnlyList<string> allowedCustodyModes, string? brokerConnection = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantId);

        public Task UpdateTenantAsync(Guid id, string name, string reportingCurrency, IReadOnlyList<string> allowedCustodyModes, string rowVersion, string? brokerConnection = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public List<BrokerConnectionItem> BrokerConnections { get; set; } = [new("simulator", "Simulator", null)];

        public List<ExecutionReportItem> ExecutionReports { get; set; } = [];

        public List<string?> ExecutionReportStatuses { get; } = [];

        public Task<IReadOnlyList<BrokerConnectionItem>> ListBrokerConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BrokerConnectionItem>>(BrokerConnections);

        public Task<IReadOnlyList<ExecutionReportItem>> ListExecutionReportsAsync(string? status, CancellationToken cancellationToken = default)
        {
            ExecutionReportStatuses.Add(status);
            return Task.FromResult<IReadOnlyList<ExecutionReportItem>>(
                ExecutionReports.Where(item => status is null || item.Status == status).ToList());
        }

        public Task DisableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnableTenantAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public List<Guid?> TenantAdminLists { get; } = [];

        public Task<TenantAdminPage> ListTenantAdminsAsync(int page, int pageSize, Guid? tenantId, bool? enabledOnly, string? search, CancellationToken cancellationToken = default)
        {
            TenantAdminLists.Add(tenantId);
            return Task.FromResult(new TenantAdminPage([], page, pageSize, 0));
        }

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

        public List<Guid> ClientMoneyLists { get; } = [];

        public Task<IReadOnlyList<ClientMoneyAccountItem>> ListClientMoneyAccountsAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            ClientMoneyLists.Add(tenantId);
            return Task.FromResult<IReadOnlyList<ClientMoneyAccountItem>>([
                new ClientMoneyAccountItem(
                    ClientMoneyUsdId, tenantId, "USD", "Simulator Bank", "North Advisory Ltd Client Funds Trust Account",
                    "02-0100-0123456-00", true, "cma-rv1", DateTimeOffset.UnixEpoch, "simulator",
                    new ClientMoneyAccountFeed(
                        new DateTimeOffset(2026, 11, 20, 2, 1, 7, TimeSpan.Zero),
                        new DateTimeOffset(2026, 11, 19, 22, 14, 0, TimeSpan.Zero),
                        "Unavailable")),
                new ClientMoneyAccountItem(
                    ClientMoneyNzdId, tenantId, "NZD", "Old Bank", "North Advisory Ltd Old Trust Account",
                    "38-9000-7654321-00", false, "cma-rv2", DateTimeOffset.UnixEpoch, "other-bank"),
                new ClientMoneyAccountItem(
                    ClientMoneyAudId, tenantId, "AUD", "Simulator Bank", "North Advisory Ltd AUD Trust Account",
                    "06-0100-0000001-00", true, "cma-rv3", DateTimeOffset.UnixEpoch, "simulator",
                    new ClientMoneyAccountFeed(
                        new DateTimeOffset(2026, 11, 19, 1, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 11, 20, 3, 5, 0, TimeSpan.Zero),
                        "UnknownAccount"))
            ]);
        }

        public Task<ClientMoneyAccountItem> GetClientMoneyAccountAsync(
            Guid tenantId,
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Guid> CreateClientMoneyAccountAsync(
            Guid tenantId,
            string currency,
            string bankName,
            string accountName,
            string accountNumber,
            string bankConnection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ClientMoneyUsdId);

        public List<BankConnectionItem> BankConnections { get; set; } = [new("simulator", "Simulator")];

        public List<MarketDataConnectionItem> MarketDataConnections { get; set; } = [];

        public List<CorporateActionAnnouncementItem> Announcements { get; set; } = [];

        public List<string?> AnnouncementStatuses { get; } = [];

        public Task<IReadOnlyList<CorporateActionAnnouncementItem>> ListCorporateActionAnnouncementsAsync(
            string? status,
            CancellationToken cancellationToken = default)
        {
            AnnouncementStatuses.Add(status);
            return Task.FromResult<IReadOnlyList<CorporateActionAnnouncementItem>>(
                Announcements.Where(item => status is null || item.Status == status).ToList());
        }

        public Task<IReadOnlyList<MarketDataConnectionItem>> ListMarketDataConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MarketDataConnectionItem>>(MarketDataConnections);

        public Task<IReadOnlyList<BankConnectionItem>> ListBankConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BankConnectionItem>>(BankConnections);

        public Task DisableClientMoneyAccountAsync(Guid tenantId, Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnableClientMoneyAccountAsync(Guid tenantId, Guid id, string rowVersion, CancellationToken cancellationToken = default) =>
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

            if (tenantId == MarketTenantId)
            {
                return Task.FromResult(new InstrumentPage(
                    [
                        new InstrumentItem(MarketInstrumentId, tenantId, "ACME.SIM", "Acme Simulated Holdings", "USD", true, "rv1", "simulator"),
                        new InstrumentItem(SecondInstrumentId, tenantId, "VTI", "Vanguard", "USD", true, "rv2")
                    ],
                    page,
                    pageSize,
                    2));
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

            if (id == MarketInstrumentId)
            {
                return Task.FromResult(new InstrumentItem(id, TenantId, "ACME.SIM", "Acme Simulated Holdings", "USD", true, "rv3", "simulator"));
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

        public List<Guid> InterestRunLists { get; } = [];

        public Task<IReadOnlyList<InterestRunItem>> ListInterestRunsAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            InterestRunLists.Add(tenantId);
            return Task.FromResult<IReadOnlyList<InterestRunItem>>([
                new InterestRunItem(Guid.NewGuid(), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31), "Scheduled", 42, 1, 37,
                    [new InterestRunTotal("USD", 25, 410.55m, 93.40m)]),
                new InterestRunItem(Guid.NewGuid(), new DateOnly(2026, 12, 15), new DateOnly(2026, 11, 30), "Manual", 42, 0, 0, [])
            ]);
        }

        public Task<Guid> StartInterestRunAsync(Guid tenantId, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid());
    }
}
