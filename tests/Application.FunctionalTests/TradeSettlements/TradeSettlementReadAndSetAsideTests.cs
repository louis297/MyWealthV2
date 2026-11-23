using System.Net;
using System.Text.Json;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R1, R15, R16: who may use the trade settlement routes, the reads, and set aside.</summary>
[Parallelizable]
public class TradeSettlementReadAndSetAsideTests : TestBase
{
    private const string Path = "/trade-settlements";

    [Test]
    public async Task TheItem_HasThePlatformsAndTheBrokersFigures_ItsFills_AndItsPayments()
    {
        var (world, clientMoney, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        await BankFeedHttp.SendAsync();

        var item = await ReadAsync(world.TenantAdmin, $"{Path}/{settlement.PublicId}");

        item.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "clientMoneyAccount", "connection", "brokerSettlementId", "settledDate", "fillCount", "netAmount", "broker",
            "status", "failure", "failureDetail", "completedAt", "statementLineId", "setAside", "payments", "fills", "rowVersion"
        ], ignoreOrder: true);
        item.GetProperty("id").GetGuid().ShouldBe(settlement.PublicId);
        var account = item.GetProperty("clientMoneyAccount");
        (account.GetProperty("id").GetGuid(), account.GetProperty("currency").GetString(), account.GetProperty("accountNumber").GetString())
            .ShouldBe((clientMoney.PublicId, "USD", clientMoney.AccountNumber));
        (item.GetProperty("connection").GetString(), item.GetProperty("brokerSettlementId").GetString())
            .ShouldBe(("simulator", "w-S1"));
        (item.GetProperty("fillCount").GetInt32(), item.GetProperty("netAmount").GetDecimal()).ShouldBe((1, -400.48m));
        var broker = item.GetProperty("broker");
        (broker.GetProperty("fillCount").GetInt32(), broker.GetProperty("netAmount").GetDecimal(),
            broker.GetProperty("settlementAccount").GetString()).ShouldBe((1, -400.48m, clientMoney.AccountNumber));
        item.GetProperty("status").GetString().ShouldBe("AwaitingPayment");
        item.GetProperty("setAside").ValueKind.ShouldBe(JsonValueKind.Null);
        var payment = item.GetProperty("payments").EnumerateArray().Single();
        (payment.GetProperty("status").GetString(), payment.GetProperty("attempts").GetInt32()).ShouldBe(("Accepted", 1));
        var fill = item.GetProperty("fills").EnumerateArray().Single();
        (fill.GetProperty("side").GetString(), fill.GetProperty("quantity").GetDecimal(), fill.GetProperty("price").GetDecimal(),
            fill.GetProperty("gross").GetDecimal(), fill.GetProperty("status").GetString()).ShouldBe(("Buy", 4m, 100.12m, 400.48m, "Posted"));
        fill.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task TheItem_OfACompletedSettlement_NamesItsStatementLine()
    {
        var (world, clientMoney, settlement) = await TradeSettlementReceiptTests.ReceivingAsync();
        var credit = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 990m, "USD", "w-S2");
        await BankFeedHttp.RunAsync();

        var item = await ReadAsync(world.TenantAdmin, $"{Path}/{settlement.PublicId}");

        item.GetProperty("status").GetString().ShouldBe("Completed");
        item.GetProperty("statementLineId").GetGuid().ShouldBe((await BankFeedHttp.LineAsync(credit.Id!)).PublicId);
    }

    [Test]
    public async Task TheList_IsNewestFirst_WithoutFillsOrPayments_AndFiltersByStatusOpenAndClientMoneyAccount()
    {
        var (world, clientMoney, paying) = await TradeSettlementPaymentTests.PayingAsync();
        await Orders.OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var (sell, sellFill) = await TradeSettlementReportsTests.FilledAsync(world, "Sell", 1m, 100m);
        var (buy, buyFill) = await TradeSettlementReportsTests.FilledAsync(world, "Buy", 1m, 100m);
        BrokerHttp.Feed.AddSettled(sell, sellFill, settlementId: "w-S7");
        BrokerHttp.Feed.AddSettled(buy, buyFill, settlementId: "w-S7");
        BrokerHttp.Feed.AddSettlement("w-S7", clientMoney.AccountNumber, 0m, 2);
        await BrokerHttp.RunReportsAsync();

        var all = await ReadAsync(world.TenantAdmin, Path);
        var open = await ReadAsync(world.TenantAdmin, $"{Path}?open=true");
        var completed = await ReadAsync(world.TenantAdmin, $"{Path}?status=Completed,SetAside");
        var byAccount = await ReadAsync(world.TenantAdmin, $"{Path}?clientMoneyAccountId={clientMoney.PublicId}");
        var otherAccount = await ReadAsync(world.TenantAdmin, $"{Path}?clientMoneyAccountId={Guid.NewGuid()}");

        Ids(all).ShouldBe(["w-S7", "w-S1"]);
        all.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var listed = all.GetProperty("items")[0];
        listed.TryGetProperty("fills", out _).ShouldBeFalse();
        listed.TryGetProperty("payments", out _).ShouldBeFalse();
        Ids(open).ShouldBe(["w-S1"]);
        Ids(completed).ShouldBe(["w-S7"]);
        Ids(byAccount).ShouldBe(["w-S7", "w-S1"]);
        Ids(otherAccount).ShouldBeEmpty();
        paying.BrokerSettlementId.ShouldBe("w-S1");
    }

    [Test]
    public async Task AnUnknownStatusFilter_OrATenantId_Returns400()
    {
        var (world, _, _) = await TradeSettlementPaymentTests.PayingAsync();

        var status = await TenantHttp.Send(HttpMethod.Get, $"{Path}?status=Paid", world.TenantAdmin);
        var tenant = await TenantHttp.Send(HttpMethod.Get, $"{Path}?tenantId={Guid.NewGuid()}", world.TenantAdmin);

        (await CustodialHttp.ErrorsAsync(status)).ShouldContainKey("Status");
        tenant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestCase(TradeSettlementStatus.AwaitingReceipt)]
    [TestCase(TradeSettlementStatus.PaymentFailed)]
    [TestCase(TradeSettlementStatus.Disputed)]
    public async Task SetAside_FromAnAllowedStatus_Returns204_AndRecordsThePersonTheTimeAndTheNote(TradeSettlementStatus status)
    {
        var (world, settlement) = await InStatusAsync(status);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await SetAsideAsync(world.TenantAdmin, settlement, "  Agreed with the broker by phone, ref 4471  ");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await ReadAsync(world.TenantAdmin, $"{Path}/{settlement.PublicId}");
        item.GetProperty("status").GetString().ShouldBe("SetAside");
        var setAside = item.GetProperty("setAside");
        setAside.GetProperty("note").GetString().ShouldBe("Agreed with the broker by phone, ref 4471");
        setAside.GetProperty("at").GetDateTimeOffset().ShouldBeGreaterThan(before);
        setAside.GetProperty("by").GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task SetAside_AwaitingPayment_BeforeTheFirstSend_CancelsThePayment()
    {
        var (world, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();

        var response = await SetAsideAsync(world.TenantAdmin, settlement, "Paid by hand");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id)).Single().Status.ShouldBe(OutgoingPaymentStatus.Cancelled);
        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.SetAside);
        await BankFeedHttp.SendAsync();
        BankFeedHttp.Payments.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task SetAside_AfterASendAttempt_Returns400AtStatus()
    {
        var (world, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        await BankFeedHttp.SendAsync();

        var response = await SetAsideAsync(world.TenantAdmin, await TradeSettlementPaymentTests.SettlementAsync(settlement.Id), "Note");

        (await CustodialHttp.ErrorsAsync(response))["Status"]
            .ShouldBe(["The bank has been asked to pay this settlement, so it cannot be set aside."]);
    }

    [Test]
    public async Task SetAside_FromCompletedOrTwice_Returns400AtStatus_AndANoteIsRequired()
    {
        var (world, settlement) = await InStatusAsync(TradeSettlementStatus.Disputed);
        (await SetAsideAsync(world.TenantAdmin, settlement, "Once")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var twice = await SetAsideAsync(world.TenantAdmin, await TradeSettlementPaymentTests.SettlementAsync(settlement.Id), "Again");
        var noNote = await TenantHttp.Send(
            HttpMethod.Post, $"{Path}/{settlement.PublicId}/set-aside", world.TenantAdmin,
            new { rowVersion = Convert.ToBase64String(settlement.RowVersion), note = "   " });

        (await CustodialHttp.ErrorsAsync(twice))["Status"].ShouldBe(["This settlement is SetAside and cannot be set aside."]);
        (await CustodialHttp.ErrorsAsync(noNote))["Note"].ShouldBe(["Note must be 1 to 200 characters."]);
    }

    [Test]
    public async Task WithoutBearer_EveryRoute_Returns401WithoutRedirect()
    {
        foreach (var (method, path, body) in Routes(Guid.NewGuid()))
        {
            var response = await TenantHttp.Send(method, path, null, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task AdviserCustomerAndSystemAdmin_Return403_OnEveryRoute()
    {
        var (world, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        var system = await TenantHttp.SignInSystemAdmin();

        foreach (var token in new[] { world.Adviser, world.CustomerToken, system.AccessToken })
        {
            foreach (var (method, path, body) in Routes(settlement.PublicId))
            {
                (await TenantHttp.Send(method, path, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            }
        }
    }

    [Test]
    public async Task AnotherTenantsSettlement_Returns404_OnEveryRoute_AndIsNotListed()
    {
        var (north, _, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        var south = await Orders.OrderWorld.CreateAsync("South Advisory", "south-advisory");

        foreach (var (method, path, body) in Routes(settlement.PublicId).Where(route => route.Path != Path))
        {
            (await TenantHttp.Send(method, path, south.TenantAdmin, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        Ids(await ReadAsync(south.TenantAdmin, Path)).ShouldBeEmpty();
        Ids(await ReadAsync(north.TenantAdmin, Path)).ShouldBe(["w-S1"]);
        (await TenantHttp.Send(HttpMethod.Get, $"{Path}/{Guid.NewGuid()}", north.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static IEnumerable<(HttpMethod Method, string Path, object? Body)> Routes(Guid id)
    {
        var body = new { rowVersion = "AAAAAAAAAAE=", note = "Note" };
        yield return (HttpMethod.Get, Path, null);
        yield return (HttpMethod.Get, $"{Path}/{id}", null);
        yield return (HttpMethod.Post, $"{Path}/{id}/pay", body);
        yield return (HttpMethod.Post, $"{Path}/{id}/set-aside", body);
    }

    internal static Task<HttpResponseMessage> SetAsideAsync(string accessToken, TradeSettlement settlement, string note) =>
        TenantHttp.Send(
            HttpMethod.Post, $"{Path}/{settlement.PublicId}/set-aside", accessToken,
            new { rowVersion = Convert.ToBase64String(settlement.RowVersion), note });

    private static async Task<(Orders.OrderWorld World, TradeSettlement Settlement)> InStatusAsync(TradeSettlementStatus status)
    {
        switch (status)
        {
            case TradeSettlementStatus.AwaitingReceipt:
                var (receiving, _, waiting) = await TradeSettlementReceiptTests.ReceivingAsync();
                return (receiving, waiting);
            case TradeSettlementStatus.PaymentFailed:
                var (failing, _, paying) = await TradeSettlementPaymentTests.PayingAsync();
                BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.Refused));
                await BankFeedHttp.SendAsync();
                return (failing, await TradeSettlementPaymentTests.SettlementAsync(paying.Id));
            default:
                var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
                var (id, fill) = await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);
                BrokerHttp.Feed.AddSettled(id, fill, settlementId: "w-S4");
                BrokerHttp.Feed.AddSettlement("w-S4", clientMoney.AccountNumber, -1m, 1);
                await BrokerHttp.RunReportsAsync();
                return (world, (await TradeSettlementReportsTests.SettlementsAsync(world.Tenant.Id)).Single());
        }
    }

    private static IReadOnlyList<string> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("brokerSettlementId").GetString()!).ToList();

    private static async Task<JsonElement> ReadAsync(string accessToken, string path)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
