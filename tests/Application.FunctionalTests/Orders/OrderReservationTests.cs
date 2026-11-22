using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;

namespace MyWealthV2.Application.FunctionalTests.Orders;

/// <summary>Orders R39–R41: reserved cash counts buy orders; a holding has a reserved and an available quantity.</summary>
[Parallelizable]
public class OrderReservationTests : TestBase
{
    [Test]
    public async Task ABuyOrder_RaisesTheAccountsReservedCash_AndLowersItsAvailableCash_OnEveryAccountRead()
    {
        var world = await OrderWorld.CreateAsync();

        await world.PlaceAsync("Buy", 10m);

        (await world.CashAsync()).ShouldBe((5000.00m, 1055.25m, 3944.75m));
        foreach (var token in new[] { world.TenantAdmin, world.Adviser, world.CustomerToken })
        {
            var item = await ReadAsync(token, $"/accounts/{world.AccountId}");
            item.GetProperty("cashBalance").GetDecimal().ShouldBe(5000.00m);
            item.GetProperty("reservedCash").GetDecimal().ShouldBe(1055.25m);
            item.GetProperty("availableCash").GetDecimal().ShouldBe(3944.75m);
        }

        var listed = (await ReadAsync(world.TenantAdmin, "/accounts")).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == world.AccountId);
        listed.GetProperty("reservedCash").GetDecimal().ShouldBe(1055.25m);
        listed.GetProperty("availableCash").GetDecimal().ShouldBe(3944.75m);
    }

    [Test]
    public async Task ReservedCash_IsTheSumOfOpenWithdrawalsAndLiveBuyOrders_AndASellOrderReservesNone()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        await CustodialHttp.RequestWithdrawalAsync(world.AccountId, 300m);
        await world.PlaceAsync("Buy", 10m);
        await world.PlaceAsync("Buy", 2m);
        await world.PlaceAsync("Sell", 30m);

        // 5 000 less the 1 000 the holding cost; 300 + 1 055.25 + 215.00 reserved.
        (await world.CashAsync()).ShouldBe((4000.00m, 1570.25m, 2429.75m));
    }

    [Test]
    public async Task ASecondBuy_ThatTogetherWithTheFirstExceedsTheAvailableCash_Returns400AtQuantity()
    {
        var world = await OrderWorld.CreateAsync(funding: 2000.00m);
        await world.PlaceAsync("Buy", 10m);

        var response = await world.TryPlaceAsync("Buy", 10m);

        (await CustodialHttp.ErrorsAsync(response))["Quantity"].ShouldBe([
            "This order can cost up to 1055.25 USD with fees. The account's available cash is 944.75 USD."
        ]);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(1);
    }

    [Test]
    public async Task AWithdrawal_OfCashABuyOrderReserves_Returns400AtAmount()
    {
        var world = await OrderWorld.CreateAsync(funding: 2000.00m);
        await world.PlaceAsync("Buy", 10m);
        var payee = await CustodialHttp.VerifiedPayeeAsync(world.AccountId);

        var response = await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, payee, 944.76m));

        (await CustodialHttp.ErrorsAsync(response))["Amount"].ShouldBe(["The account's available cash is 944.75 USD."]);
        (await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, payee, 944.75m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AHandWrite_ThatWouldTakeCashABuyOrderReserves_Returns400_NamingWithdrawalsAndOrders()
    {
        var world = await OrderWorld.CreateAsync(funding: 2000.00m);
        await world.PlaceAsync("Buy", 10m);

        var refused = await CustodialHttp.PostAsync(
            world.TenantAdmin, AdjustmentWorld.Body(world.AccountId, amount: -944.76m), AdjustmentWorld.Path);
        var allowed = await CustodialHttp.PostAsync(
            world.TenantAdmin, AdjustmentWorld.Body(world.AccountId, amount: -944.75m), AdjustmentWorld.Path);

        (await CustodialHttp.ErrorsAsync(refused))[""]
            .ShouldBe(["Available cash cannot become negative: 1055.25 USD is reserved for withdrawals and orders."]);
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());
        (await world.CashAsync()).ShouldBe((1055.25m, 1055.25m, 0m));
    }

    [Test]
    public async Task AHolding_ShowsTheQuantityItsSellOrdersReserve_AndWhatIsAvailable_ToEveryReader()
    {
        var world = await OrderWorld.CreateAsync();
        var other = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "BETA", price: 20m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 100m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, other, 5m, price: 20m);
        await world.PlaceAsync("Sell", 30m);
        await world.PlaceAsync("Sell", 10m);
        await world.PlaceAsync("Buy", 1m);

        foreach (var token in new[] { world.TenantAdmin, world.Adviser, world.CustomerToken })
        {
            var items = (await ReadAsync(token, $"/accounts/{world.AccountId}/holdings")).GetProperty("items").EnumerateArray().ToList();
            var acme = items.Single(item => item.GetProperty("symbol").GetString() == "ACME");
            acme.GetProperty("quantity").GetDecimal().ShouldBe(100m);
            acme.GetProperty("reservedQuantity").GetDecimal().ShouldBe(40m);
            acme.GetProperty("availableQuantity").GetDecimal().ShouldBe(60m);
            var beta = items.Single(item => item.GetProperty("symbol").GetString() == "BETA");
            beta.GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
            beta.GetProperty("availableQuantity").GetDecimal().ShouldBe(5m);
        }
    }

    [Test]
    public async Task AnAdjustment_ThatWouldLowerAHoldingBelowItsReservedQuantity_Returns400AtSecurity()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 100m);
        await world.PlaceAsync("Sell", 40m);

        var refused = await CustodialHttp.PostAsync(
            world.TenantAdmin,
            AdjustmentWorld.Body(world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, -60.5m, -1512.5m)),
            AdjustmentWorld.Path);
        var allowed = await CustodialHttp.PostAsync(
            world.TenantAdmin,
            AdjustmentWorld.Body(world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, -60m, -1500m)),
            AdjustmentWorld.Path);

        (await CustodialHttp.ErrorsAsync(refused))["Security"].ShouldBe(["40 ACME is reserved for sell orders."]);
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());
        var holding = (await ReadAsync(world.TenantAdmin, $"/accounts/{world.AccountId}/holdings")).GetProperty("items").EnumerateArray().Single();
        holding.GetProperty("quantity").GetDecimal().ShouldBe(40m);
        holding.GetProperty("availableQuantity").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task AHoldingOnAnExternalAccount_ReservesNothing()
    {
        var world = await OrderWorld.CreateAsync();
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);
        var stockIn = CustodialHttp.Trade(external, "StockIn", world.InstrumentId, 7m).WithBookedAt();
        stockIn["security"] = new[] { new { instrumentId = world.InstrumentId, quantity = 7m, cost = 700m } };
        (await CustodialHttp.PostAsync(world.TenantAdmin, stockIn)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var holding = (await ReadAsync(world.TenantAdmin, $"/accounts/{external}/holdings")).GetProperty("items").EnumerateArray().Single();

        holding.GetProperty("quantity").GetDecimal().ShouldBe(7m);
        holding.GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
        holding.GetProperty("availableQuantity").GetDecimal().ShouldBe(7m);
    }

    private static async Task<JsonElement> ReadAsync(string accessToken, string path)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
