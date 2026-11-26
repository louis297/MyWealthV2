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
        var item = await platform.GetTenantAsync(TenantId);
        await platform.CreateTenantAsync("North", "north", "nzd", ["External"]);
        await platform.UpdateTenantAsync(TenantId, "North Ltd", "AUD", ["Custodial"], "rv1");
        await platform.DisableTenantAsync(TenantId, "rv1");
        await platform.EnableTenantAsync(TenantId, "rv2");

        AssertRequest(api.Requests[0], "GET", "/tenants?page=1&pageSize=20&isEnabled=true&search=north");
        AssertRequest(api.Requests[1], "GET", $"/tenants/{TenantId}");
        AssertRequest(api.Requests[2], "POST", "/tenants");
        Body(api.Requests[2]).GetProperty("name").GetString().ShouldBe("North");
        Body(api.Requests[2]).GetProperty("code").GetString().ShouldBe("north");
        Body(api.Requests[2]).GetProperty("reportingCurrency").GetString().ShouldBe("nzd");
        Modes(Body(api.Requests[2])).ShouldBe(["External"]);
        item.AllowedCustodyModes.ShouldBe(["External", "Custodial"]);
        AssertRequest(api.Requests[3], "PUT", $"/tenants/{TenantId}");
        Body(api.Requests[3]).GetProperty("name").GetString().ShouldBe("North Ltd");
        Body(api.Requests[3]).GetProperty("reportingCurrency").GetString().ShouldBe("AUD");
        Body(api.Requests[3]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        Modes(Body(api.Requests[3])).ShouldBe(["Custodial"]);
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
    public async Task ClientMoneyAccountCalls_UseTheTenantRoutes_AndSendTheRowVersion()
    {
        var rowId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var item = $$"""
            {"id":"{{rowId}}","tenantId":"{{TenantId}}","currency":"USD","bankName":"Simulator Bank","accountName":"North Trust","accountNumber":"02-0100","bankConnection":"simulator","isActive":true,"bankFeed":{"lastSucceededAt":"2026-11-20T02:01:07Z","lastFailedAt":null,"lastFailure":null},"rowVersion":"rv1","created":"2026-11-19T03:00:00+00:00"}
            """;
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, $$"""{"items":[{{item}}],"page":1,"pageSize":100,"totalCount":1}""");
        api.Enqueue(HttpStatusCode.OK, item);
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{rowId}}"}""");
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        var listed = (await platform.ListClientMoneyAccountsAsync(TenantId)).ShouldHaveSingleItem();
        var fetched = await platform.GetClientMoneyAccountAsync(TenantId, rowId);
        var created = await platform.CreateClientMoneyAccountAsync(TenantId, "USD", "Simulator Bank", "North Trust", "02-0100", "simulator");
        await platform.DisableClientMoneyAccountAsync(TenantId, rowId, "rv1");
        await platform.EnableClientMoneyAccountAsync(TenantId, rowId, "rv2");

        listed.Id.ShouldBe(rowId);
        listed.Currency.ShouldBe("USD");
        listed.BankName.ShouldBe("Simulator Bank");
        listed.AccountName.ShouldBe("North Trust");
        listed.AccountNumber.ShouldBe("02-0100");
        listed.IsActive.ShouldBeTrue();
        listed.RowVersion.ShouldBe("rv1");
        listed.BankConnection.ShouldBe("simulator");
        listed.BankFeed.ShouldNotBeNull().LastSucceededAt.ShouldBe(new DateTimeOffset(2026, 11, 20, 2, 1, 7, TimeSpan.Zero));
        listed.BankFeed.LastFailure.ShouldBeNull();
        fetched.Id.ShouldBe(rowId);
        created.ShouldBe(rowId);
        AssertRequest(api.Requests[0], "GET", $"/tenants/{TenantId}/client-money-accounts?pageSize=100");
        AssertRequest(api.Requests[1], "GET", $"/tenants/{TenantId}/client-money-accounts/{rowId}");
        AssertRequest(api.Requests[2], "POST", $"/tenants/{TenantId}/client-money-accounts");
        var body = Body(api.Requests[2]);
        body.GetProperty("currency").GetString().ShouldBe("USD");
        body.GetProperty("bankName").GetString().ShouldBe("Simulator Bank");
        body.GetProperty("accountName").GetString().ShouldBe("North Trust");
        body.GetProperty("accountNumber").GetString().ShouldBe("02-0100");
        body.GetProperty("bankConnection").GetString().ShouldBe("simulator");
        body.EnumerateObject().Count().ShouldBe(5);
        AssertRequest(api.Requests[3], "POST", $"/tenants/{TenantId}/client-money-accounts/{rowId}/disable");
        Body(api.Requests[3]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        AssertRequest(api.Requests[4], "POST", $"/tenants/{TenantId}/client-money-accounts/{rowId}/enable");
        Body(api.Requests[4]).GetProperty("rowVersion").GetString().ShouldBe("rv2");
    }

    [Test]
    public async Task SafekeepingAccountCalls_UseTheTenantRoutes_AndSendTheRowVersion()
    {
        // In-kind transfers BO30.
        var rowId = Guid.Parse("88888888-8888-8888-8888-8888888888bb");
        var item = $$"""
            {"id":"{{rowId}}","currency":"USD","custodianName":"Simulator Custody","accountName":"Demo Advisory Nominees","accountNumber":"SIM-DEMO-CUSTODY-USD","custodianConnection":"simulator","isActive":true,"custodianFeed":{"lastSucceededAt":"2026-11-24T02:01:07Z","lastFailedAt":null,"lastFailure":null,"positionsAt":"2026-11-24T02:01:00Z"},"rowVersion":"rv1"}
            """;
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, $$"""{"items":[{{item}}],"page":1,"pageSize":100,"totalCount":1}""");
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{rowId}}"}""");
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        var listed = (await platform.ListSafekeepingAccountsAsync(TenantId)).ShouldHaveSingleItem();
        var created = await platform.CreateSafekeepingAccountAsync(
            TenantId, "USD", "Simulator Custody", "Demo Advisory Nominees", "SIM-DEMO-CUSTODY-USD", "simulator");
        await platform.DisableSafekeepingAccountAsync(TenantId, rowId, "rv1");
        await platform.EnableSafekeepingAccountAsync(TenantId, rowId, "rv2");

        listed.Id.ShouldBe(rowId);
        listed.CustodianName.ShouldBe("Simulator Custody");
        listed.AccountNumber.ShouldBe("SIM-DEMO-CUSTODY-USD");
        listed.CustodianConnection.ShouldBe("simulator");
        listed.RowVersion.ShouldBe("rv1");
        listed.CustodianFeed.ShouldNotBeNull().PositionsAt.ShouldBe(new DateTimeOffset(2026, 11, 24, 2, 1, 0, TimeSpan.Zero));
        created.ShouldBe(rowId);
        AssertRequest(api.Requests[0], "GET", $"/tenants/{TenantId}/safekeeping-accounts?pageSize=100");
        AssertRequest(api.Requests[1], "POST", $"/tenants/{TenantId}/safekeeping-accounts");
        var body = Body(api.Requests[1]);
        body.GetProperty("currency").GetString().ShouldBe("USD");
        body.GetProperty("custodianName").GetString().ShouldBe("Simulator Custody");
        body.GetProperty("accountName").GetString().ShouldBe("Demo Advisory Nominees");
        body.GetProperty("accountNumber").GetString().ShouldBe("SIM-DEMO-CUSTODY-USD");
        body.GetProperty("custodianConnection").GetString().ShouldBe("simulator");
        body.EnumerateObject().Count().ShouldBe(5);
        AssertRequest(api.Requests[2], "POST", $"/tenants/{TenantId}/safekeeping-accounts/{rowId}/disable");
        Body(api.Requests[2]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        AssertRequest(api.Requests[3], "POST", $"/tenants/{TenantId}/safekeeping-accounts/{rowId}/enable");
        Body(api.Requests[3]).GetProperty("rowVersion").GetString().ShouldBe("rv2");
    }

    [Test]
    public async Task CustodianConnections_ReadsTheList()
    {
        // In-kind transfers BO31.
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """{"items":[{"name":"simulator","adapter":"Simulator"}],"page":1,"pageSize":20,"totalCount":1}""");
        var platform = Create(api);

        var connections = await platform.ListCustodianConnectionsAsync();

        connections.ShouldBe([new CustodianConnectionItem("simulator", "Simulator")]);
        AssertRequest(api.Requests[0], "GET", "/custodian-connections");
    }

    [Test]
    public async Task BankConnections_ReadsTheList()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """{"items":[{"name":"simulator","adapter":"Simulator"}],"page":1,"pageSize":20,"totalCount":1}""");
        var platform = Create(api);

        var connections = await platform.ListBankConnectionsAsync();

        connections.ShouldBe([new BankConnectionItem("simulator", "Simulator")]);
        AssertRequest(api.Requests[0], "GET", "/bank-connections");
    }

    [Test]
    public async Task Tenant_CarriesTheBrokerConnection_OnTheItem_OnCreate_AndOnUpdateOnlyWhenGiven()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, TenantJson().Replace("\"isActive\"", "\"brokerConnection\":\"simulator\",\"isActive\""));
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{TenantId}}"}""");
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{TenantId}}"}""");
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        var item = await platform.GetTenantAsync(TenantId);
        await platform.CreateTenantAsync("North", "north", "NZD", ["Custodial"], "simulator");
        await platform.CreateTenantAsync("South", "south", "NZD", ["Custodial"]);
        await platform.UpdateTenantAsync(TenantId, "North Ltd", "NZD", ["Custodial"], "rv1", "other-broker");
        await platform.UpdateTenantAsync(TenantId, "North Ltd", "NZD", ["Custodial"], "rv2");

        item.BrokerConnection.ShouldBe("simulator");
        Body(api.Requests[1]).GetProperty("brokerConnection").GetString().ShouldBe("simulator");
        Body(api.Requests[2]).GetProperty("brokerConnection").ValueKind.ShouldBe(JsonValueKind.Null);
        Body(api.Requests[3]).GetProperty("brokerConnection").GetString().ShouldBe("other-broker");
        Body(api.Requests[3]).GetProperty("rowVersion").GetString().ShouldBe("rv1");
        Body(api.Requests[4]).GetProperty("brokerConnection").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task BrokerConnections_ReadsTheListWithTheReportsState()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """
            {"items":[
              {"name":"simulator","adapter":"Simulator",
               "reports":{"lastSucceededAt":"2026-11-23T02:13:00Z","lastFailedAt":"2026-11-23T02:14:00Z","lastFailure":"InvalidItem"}},
              {"name":"other","adapter":"Simulator","reports":null}
            ],"page":1,"pageSize":20,"totalCount":2}
            """);
        var platform = Create(api);

        var connections = await platform.ListBrokerConnectionsAsync();

        connections.ShouldBe([
            new BrokerConnectionItem("simulator", "Simulator", new BrokerFeedState(
                new DateTimeOffset(2026, 11, 23, 2, 13, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 23, 2, 14, 0, TimeSpan.Zero), "InvalidItem")),
            new BrokerConnectionItem("other", "Simulator", null)
        ]);
        AssertRequest(api.Requests[0], "GET", "/broker-connections");
    }

    [Test]
    public async Task ExecutionReports_ReadsTheNewestTwenty_WithTheStatusFilter()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """
            {"items":[{"id":"5a1c0000-0000-0000-0000-0000000000c7","connection":"simulator","outsideId":"w-X1","kind":null,
              "clientOrderId":"2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11","brokerOrderId":"w-O1","quantity":4,"price":100.12,
              "tradeDate":"2026-11-23","settlementDate":"2026-11-25","contractNote":"CN-1","fillOutsideId":null,"settledDate":null,
              "reason":null,"receivedAt":"2026-11-23T02:13:00Z","status":"Skipped","skipReason":"UnsupportedKind",
              "processedAt":"2026-11-23T02:13:01Z"}],"page":1,"pageSize":20,"totalCount":1}
            """);
        api.Enqueue(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        var platform = Create(api);

        var items = await platform.ListExecutionReportsAsync(null);
        await platform.ListExecutionReportsAsync("Skipped");

        var item = items.ShouldHaveSingleItem();
        (item.OutsideId, item.Kind, item.Quantity, item.Price, item.TradeDate, item.Status, item.SkipReason).ShouldBe(
            ("w-X1", (string?)null, (decimal?)4m, (decimal?)100.12m, (DateOnly?)new DateOnly(2026, 11, 23), "Skipped", (string?)"UnsupportedKind"));
        AssertRequest(api.Requests[0], "GET", "/execution-reports?page=1&pageSize=20");
        AssertRequest(api.Requests[1], "GET", "/execution-reports?page=1&pageSize=20&status=Skipped");
    }

    [Test]
    public async Task MarketDataConnections_ReadsTheListWithTheFeedState()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """
            {"items":[
              {"name":"simulator","adapter":"Simulator","symbolSuffix":".SIM",
               "prices":{"lastSucceededAt":"2026-11-22T03:15:01Z","lastFailedAt":null,"lastFailure":null},
               "corporateActions":{"lastSucceededAt":null,"lastFailedAt":"2026-11-22T03:15:01Z","lastFailure":"InvalidItem"}},
              {"name":"other","adapter":"Simulator","symbolSuffix":".OTH","prices":null,"corporateActions":null}
            ],"page":1,"pageSize":20,"totalCount":2}
            """);
        var platform = Create(api);

        var connections = await platform.ListMarketDataConnectionsAsync();

        connections.ShouldBe([
            new MarketDataConnectionItem("simulator", "Simulator", ".SIM",
                new MarketDataFeed(new DateTimeOffset(2026, 11, 22, 3, 15, 1, TimeSpan.Zero), null, null),
                new MarketDataFeed(null, new DateTimeOffset(2026, 11, 22, 3, 15, 1, TimeSpan.Zero), "InvalidItem")),
            new MarketDataConnectionItem("other", "Simulator", ".OTH", null, null)
        ]);
        AssertRequest(api.Requests[0], "GET", "/market-data-connections");
    }

    [Test]
    public async Task CorporateActionAnnouncements_ReadsTheNewestTwenty_WithTheStatusFilter()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, """
            {"items":[{"id":"5a1c0000-0000-0000-0000-0000000000c7","connection":"simulator","outsideId":"w-CA1-V1","partyActionId":"w-CA1",
              "version":1,"partyStatus":"Announced","symbol":"ACME.SIM","currency":"USD","kind":null,"ratioNew":2,"ratioHeld":1,
              "exDate":"2026-11-24","recordDate":null,"paymentDate":null,"receivedAt":"2026-11-22T03:15:02Z","status":"Skipped",
              "skipReason":"UnsupportedKind","processedAt":"2026-11-22T03:15:03Z"}],"page":1,"pageSize":20,"totalCount":1}
            """);
        api.Enqueue(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        var platform = Create(api);

        var items = await platform.ListCorporateActionAnnouncementsAsync(null);
        await platform.ListCorporateActionAnnouncementsAsync("Skipped");

        var item = items.ShouldHaveSingleItem();
        (item.OutsideId, item.Kind, item.Status, item.SkipReason, item.ExDate).ShouldBe(
            ("w-CA1-V1", (string?)null, "Skipped", (string?)"UnsupportedKind", new DateOnly(2026, 11, 24)));
        AssertRequest(api.Requests[0], "GET", "/corporate-action-announcements?page=1&pageSize=20");
        AssertRequest(api.Requests[1], "GET", "/corporate-action-announcements?page=1&pageSize=20&status=Skipped");
    }

    [Test]
    public async Task InstrumentCalls_ListUsesTenantAndFilters()
    {
        var instrumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, $$"""
            {"items":[{"id":"{{instrumentId}}","tenantId":"{{TenantId}}","symbol":"VTI","name":"Vanguard","quoteCurrency":"USD","isActive":true,"rowVersion":"rv1"}],"page":1,"pageSize":20,"totalCount":1}
            """);
        var platform = Create(api);

        var page = await platform.ListInstrumentsAsync(1, 20, TenantId, true, "vti");

        var item = page.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(instrumentId);
        item.TenantId.ShouldBe(TenantId);
        item.Symbol.ShouldBe("VTI");
        item.Name.ShouldBe("Vanguard");
        item.QuoteCurrency.ShouldBe("USD");
        item.IsActive.ShouldBeTrue();
        item.RowVersion.ShouldBe("rv1");
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.TotalCount.ShouldBe(1);
        AssertRequest(
            api.Requests[0],
            "GET",
            $"/instruments?tenantId={TenantId}&page=1&pageSize=20&enabledOnly=true&search=vti");
    }

    [Test]
    public async Task InstrumentCalls_CreateSendsTenantSymbolNameAndQuoteCurrency()
    {
        var instrumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{instrumentId}}"}""");
        var platform = Create(api);

        var id = await platform.CreateInstrumentAsync(TenantId, "vti", "Vanguard", "usd");

        id.ShouldBe(instrumentId);
        AssertRequest(api.Requests[0], "POST", "/instruments");
        var body = Body(api.Requests[0]);
        body.GetProperty("tenantId").GetGuid().ShouldBe(TenantId);
        body.GetProperty("symbol").GetString().ShouldBe("vti");
        body.GetProperty("name").GetString().ShouldBe("Vanguard");
        body.GetProperty("quoteCurrency").GetString().ShouldBe("usd");
        body.EnumerateObject().Count().ShouldBe(4);
    }

    [Test]
    public async Task InstrumentCalls_GetAndRenameUseTheInstrumentIdOnly()
    {
        var instrumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, InstrumentJson(instrumentId));
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        var item = await platform.GetInstrumentAsync(instrumentId);
        await platform.UpdateInstrumentAsync(instrumentId, "VOO", "Vanguard 500", "rv1");

        item.Symbol.ShouldBe("VTI");
        item.QuoteCurrency.ShouldBe("USD");
        item.RowVersion.ShouldBe("rv1");
        AssertRequest(api.Requests[0], "GET", $"/instruments/{instrumentId}");
        api.Requests[0].Path.ShouldNotContain("tenantId");
        AssertRequest(api.Requests[1], "PUT", $"/instruments/{instrumentId}");
        var body = Body(api.Requests[1]);
        body.GetProperty("symbol").GetString().ShouldBe("VOO");
        body.GetProperty("name").GetString().ShouldBe("Vanguard 500");
        body.GetProperty("rowVersion").GetString().ShouldBe("rv1");
        body.TryGetProperty("quoteCurrency", out _).ShouldBeFalse();
        body.TryGetProperty("tenantId", out _).ShouldBeFalse();
        body.TryGetProperty("isActive", out _).ShouldBeFalse();
        body.EnumerateObject().Count().ShouldBe(3);
    }

    [Test]
    public async Task InstrumentCalls_DisableAndEnableSendRowVersionOnly()
    {
        var instrumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.NoContent, "");
        api.Enqueue(HttpStatusCode.NoContent, "");
        var platform = Create(api);

        await platform.DisableInstrumentAsync(instrumentId, "rv1");
        await platform.EnableInstrumentAsync(instrumentId, "rv2");

        AssertRequest(api.Requests[0], "POST", $"/instruments/{instrumentId}/disable");
        var disable = Body(api.Requests[0]);
        disable.GetProperty("rowVersion").GetString().ShouldBe("rv1");
        disable.TryGetProperty("tenantId", out _).ShouldBeFalse();
        disable.EnumerateObject().Count().ShouldBe(1);
        AssertRequest(api.Requests[1], "POST", $"/instruments/{instrumentId}/enable");
        var enable = Body(api.Requests[1]);
        enable.GetProperty("rowVersion").GetString().ShouldBe("rv2");
        enable.EnumerateObject().Count().ShouldBe(1);
    }

    [Test]
    public async Task InterestRunCalls_ReadTheNewestTenOfTheTenant_AndStartARunForADate()
    {
        var runId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, $$"""
            {"items":[{"id":"{{runId}}","asOfDate":"2026-12-31","postingDate":"2026-12-31","trigger":"Scheduled","ranBy":null,"accountId":null,
              "startedAt":"2027-01-01T00:30:02Z","completedAt":"2027-01-01T00:30:09Z","accountsProcessed":42,"accountsFailed":1,"postingCount":37,
              "totals":[{"currency":"USD","postingCount":25,"grossAmount":410.55,"taxAmount":93.40}]}],"page":1,"pageSize":10,"totalCount":1}
            """);
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{runId}}"}""");
        var platform = Create(api);

        var run = (await platform.ListInterestRunsAsync(TenantId)).ShouldHaveSingleItem();
        var started = await platform.StartInterestRunAsync(TenantId, new DateOnly(2026, 12, 31));

        run.Id.ShouldBe(runId);
        run.AsOfDate.ShouldBe(new DateOnly(2026, 12, 31));
        run.Trigger.ShouldBe("Scheduled");
        (run.AccountsProcessed, run.AccountsFailed, run.PostingCount).ShouldBe((42, 1, 37));
        run.Totals.ShouldHaveSingleItem().ShouldBe(new InterestRunTotal("USD", 25, 410.55m, 93.40m));
        started.ShouldBe(runId);
        AssertRequest(api.Requests[0], "GET", $"/interest/runs?tenantId={TenantId}&pageSize=10");
        AssertRequest(api.Requests[1], "POST", "/interest/runs");
        var body = Body(api.Requests[1]);
        body.GetProperty("tenantId").GetGuid().ShouldBe(TenantId);
        body.GetProperty("asOfDate").GetString().ShouldBe("2026-12-31");
        body.EnumerateObject().Count().ShouldBe(2);
    }

    [Test]
    public async Task ValuationRunCalls_ReadTheNewestTenOfTheTenant_AndStartARunThroughADate()
    {
        var runId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.OK, $$"""
            {"items":[{"id":"{{runId}}","throughDate":"2026-03-04","trigger":"Scheduled","ranBy":null,
              "startedAt":"2026-03-06T13:00:01Z","completedAt":"2026-03-06T13:00:03Z","accountsProcessed":1,"accountsFailed":0,
              "daysValued":2,"daysIncomplete":0}],"page":1,"pageSize":10,"totalCount":1}
            """);
        api.Enqueue(HttpStatusCode.Created, $$"""{"id":"{{runId}}"}""");
        var platform = Create(api);

        var run = (await platform.ListValuationRunsAsync(TenantId)).ShouldHaveSingleItem();
        var started = await platform.StartValuationRunAsync(TenantId, new DateOnly(2026, 3, 11));

        run.ShouldBe(new ValuationRunItem(runId, new DateOnly(2026, 3, 4), "Scheduled", 1, 0, 2, 0));
        started.ShouldBe(runId);
        AssertRequest(api.Requests[0], "GET", $"/valuation-runs?tenantId={TenantId}&pageSize=10");
        AssertRequest(api.Requests[1], "POST", "/valuation-runs");
        var body = Body(api.Requests[1]);
        body.GetProperty("tenantId").GetGuid().ShouldBe(TenantId);
        body.GetProperty("throughDate").GetString().ShouldBe("2026-03-11");
        body.EnumerateObject().Count().ShouldBe(2);
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
            new MemoryTokens(),
            new BackOfficeTokenRefresh(
                new HttpClient(new ScriptedHandler()),
                () => "http://identity/connect/token",
                BackOfficeTestSecret.Value,
                TimeProvider.System));

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

    private static string?[] Modes(JsonElement body) =>
        body.TryGetProperty("allowedCustodyModes", out var modes)
            ? modes.EnumerateArray().Select(mode => mode.GetString()).ToArray()
            : [];

    private static string TenantJson() =>
        $$"""
        {"id":"{{TenantId}}","name":"North","code":"north","reportingCurrency":"NZD","allowedCustodyModes":["External","Custodial"],"isActive":true,"rowVersion":"rv1","created":"2026-10-25T00:00:00+00:00"}
        """;

    private static string InstrumentJson(Guid id) =>
        $$"""
        {"id":"{{id}}","tenantId":"{{TenantId}}","symbol":"VTI","name":"Vanguard","quoteCurrency":"USD","isActive":true,"rowVersion":"rv1"}
        """;

    private static string AdminJson() =>
        $$"""
        {"id":"{{PersonId}}","tenantId":"{{TenantId}}","name":"Alex","email":"alex@firm","status":"active","isActive":true,"rowVersion":"rv1","created":"2026-10-25T00:00:00+00:00"}
        """;

    private sealed class MemoryTokens : IAccessTokens
    {
        public string? AccessToken { get; private set; } = "access";

        public string? RefreshToken { get; private set; } = "refresh";

        public void Store(string accessToken, string refreshToken, DateTimeOffset expiresAt)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            _ = expiresAt;
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
