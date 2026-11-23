using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Transactions;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R38: the transaction item of a fill carries its execution and names its order as the source.</summary>
[Parallelizable]
public class OrderFillReadTests : TestBase
{
    private static async Task<(OrderWorld World, Guid OrderId, Guid FillId, Guid TransactionId)> FilledAsync()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        BrokerHttp.Feed.AddFill(id, 4m, 100.12m, "CN-1");
        await BrokerHttp.RunReportsAsync();
        var fill = (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("fills").EnumerateArray().Single();
        return (world, id, fill.GetProperty("id").GetGuid(), fill.GetProperty("transactionId").GetGuid());
    }

    [Test]
    public async Task TheTransactionOfAFill_CarriesItsExecution_AndItsOrderAsTheSource_ForEveryReader()
    {
        var (world, orderId, fillId, transactionId) = await FilledAsync();
        var day = OrderHttp.Today;

        foreach (var token in new[] { world.TenantAdmin, world.Adviser, world.CustomerToken })
        {
            using var json = await CustodialHttp.GetTransactionAsync(token, transactionId);
            var item = json.RootElement;
            item.GetProperty("type").GetString().ShouldBe("Buy");
            item.GetProperty("memo").GetString().ShouldBe("Buy order");
            item.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
            var source = item.GetProperty("source");
            source.GetProperty("kind").GetString().ShouldBe("OrderFill");
            source.GetProperty("id").GetGuid().ShouldBe(orderId);
            var execution = item.GetProperty("execution");
            execution.EnumerateObject().Select(property => property.Name).ShouldBe([
                "orderId", "fillId", "price", "tradeDate", "settlementDate", "brokerReference", "settledAt", "brokenAt"
            ], ignoreOrder: true);
            execution.GetProperty("orderId").GetGuid().ShouldBe(orderId);
            execution.GetProperty("fillId").GetGuid().ShouldBe(fillId);
            execution.GetProperty("price").GetDecimal().ShouldBe(100.12m);
            execution.GetProperty("tradeDate").GetString().ShouldBe(day.ToString("yyyy-MM-dd"));
            execution.GetProperty("settlementDate").GetString().ShouldBe(day.AddDays(2).ToString("yyyy-MM-dd"));
            execution.GetProperty("brokerReference").GetString().ShouldBe("CN-1");
            execution.GetProperty("settledAt").ValueKind.ShouldBe(JsonValueKind.Null);
            var fee = item.GetProperty("cash").EnumerateArray().Single(leg => leg.GetProperty("role").GetString() == "Fee");
            fee.GetProperty("amount").GetDecimal().ShouldBe(-5.00m);
            fee.GetProperty("fee").GetProperty("basisAmount").GetDecimal().ShouldBe(400.48m);
        }
    }

    [Test]
    public async Task TheTransactionList_CarriesTheExecutionOfAFill_AndNoneOnAnyOtherTransaction()
    {
        var (world, orderId, _, transactionId) = await FilledAsync();

        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions?accountId={world.AccountId}", world.CustomerToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();

        var fill = items.Single(item => item.GetProperty("id").GetGuid() == transactionId);
        fill.GetProperty("execution").GetProperty("orderId").GetGuid().ShouldBe(orderId);
        var deposit = items.Single(item => item.GetProperty("id").GetGuid() != transactionId);
        deposit.GetProperty("execution").ValueKind.ShouldBe(JsonValueKind.Null);
        deposit.GetProperty("source").GetProperty("kind").GetString().ShouldBe("BankFeed");
    }

    [Test]
    public async Task ATradeTypedBeforeThisSlice_HasNoExecution()
    {
        var world = await OrderWorld.CreateAsync();
        var handBuy = await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);

        using var json = await CustodialHttp.GetTransactionAsync(world.TenantAdmin, handBuy);

        json.RootElement.GetProperty("execution").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("source").GetProperty("kind").GetString().ShouldBe("Manual");
    }
}
