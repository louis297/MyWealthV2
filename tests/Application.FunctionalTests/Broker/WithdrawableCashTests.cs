using System.Net;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R45–R47: cash leaves a Custodial account only from its withdrawable cash; an account with a live
/// order or an unsettled fill cannot be closed.</summary>
[Parallelizable]
public class WithdrawableCashTests : TestBase
{
    private const string LeavingRefusal = "Cash from sales that have not settled cannot leave the account: 985.00 USD settles later.";

    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    /// <summary>40 ACME bought by hand at 25.00, then a sell order of 10 filled at 99.00: the balance is 4 985.00, of
    /// which 985.00 has not settled, so 4 000.00 is withdrawable.</summary>
    private static async Task<(OrderWorld World, Guid OrderId, string FillReport)> SoldAsync()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var id = await world.PlaceAsync("Sell", 10m);
        await BrokerHttp.SendAsync();
        var fill = Feed.AddFill(id, 10m, 99m);
        await BrokerHttp.RunReportsAsync();
        return (world, id, fill.Id!);
    }

    private static Dictionary<string, object?> Transfer(Guid from, Guid to, decimal amount) => new()
    {
        ["fromAccountId"] = from,
        ["toAccountId"] = to,
        ["amount"] = amount
    };

    [Test]
    public async Task AWithdrawal_AboveTheWithdrawableCash_Returns400AtAmount_AndWithinItIsAccepted()
    {
        var (world, _, _) = await SoldAsync();
        var payee = await CustodialHttp.VerifiedPayeeAsync(world.AccountId);

        var refused = await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, payee, 4000.01m));
        var accepted = await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, payee, 4000.00m));

        (await CustodialHttp.ErrorsAsync(refused))["Amount"].ShouldBe([
            "The account's withdrawable cash is 4000.00 USD. 985.00 USD is from sales that have not settled."
        ]);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AfterTheFillSettles_TheWholeBalanceCanBeWithdrawn()
    {
        var (world, id, fillReport) = await SoldAsync();
        var payee = await CustodialHttp.VerifiedPayeeAsync(world.AccountId);
        Feed.AddSettled(id, fillReport);
        await BrokerHttp.RunReportsAsync();

        var response = await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, payee, 4985.00m));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task ABuyOrder_AndANegativeAdjustment_MayUseCashThatHasNotSettled()
    {
        var (world, _, _) = await SoldAsync();

        // 45 at the limit of 105.00 is 4 725.00, plus a fee of 23.63: more than the 4 000.00 that is withdrawable.
        var order = await world.TryPlaceAsync("Buy", 45m);
        order.StatusCode.ShouldBe(HttpStatusCode.Created, await order.Content.ReadAsStringAsync());
        var adjustment = await AdjustmentWorld.PostAsync(
            world.TenantAdmin, AdjustmentWorld.Body(world.AccountId, amount: -200m));
        adjustment.StatusCode.ShouldBe(HttpStatusCode.Created, await adjustment.Content.ReadAsStringAsync());

        (await world.CashAsync()).ShouldBe((4785.00m, 4748.63m, 36.37m));
    }

    [Test]
    public async Task AnInternalTransfer_ThatWouldTakeUnsettledCashOut_Returns400_AndWithinTheWithdrawableCashIsAccepted()
    {
        var (world, _, _) = await SoldAsync();
        var second = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Second");

        var refused = await CustodialHttp.PostAsync(world.TenantAdmin, Transfer(world.AccountId, second, 4000.01m), "/transactions/transfer");
        var accepted = await CustodialHttp.PostAsync(world.TenantAdmin, Transfer(world.AccountId, second, 4000.00m), "/transactions/transfer");

        (await CustodialHttp.ErrorsAsync(refused))[""].ShouldBe([LeavingRefusal]);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
        (await world.CashAsync()).Cash.ShouldBe(985.00m);
    }

    [Test]
    public async Task AnFxConversion_AndItsQuote_ThatWouldTakeUnsettledCashOut_Return400()
    {
        var (world, _, _) = await SoldAsync();
        var nzd = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Kiwi", "NZD");
        await FxConversionTests.SetRateAsync("USD", "NZD", 2.00000000m, FxConversionTests.RateAsOf);
        var quoteTooMuch = new Dictionary<string, object?> { ["fromAccountId"] = world.AccountId, ["toAccountId"] = nzd, ["sellAmount"] = 4000.01m };
        var quoteWithin = new Dictionary<string, object?> { ["fromAccountId"] = world.AccountId, ["toAccountId"] = nzd, ["sellAmount"] = 4000.00m };

        var quoteRefused = await TransactionHttp.Send(HttpMethod.Post, "/transactions/fx/quote", world.TenantAdmin, quoteTooMuch, idempotencyKey: null);
        var quoteAccepted = await TransactionHttp.Send(HttpMethod.Post, "/transactions/fx/quote", world.TenantAdmin, quoteWithin, idempotencyKey: null);
        var refused = await FxConversionTests.PostAsync(
            world.TenantAdmin, FxConversionTests.SellBody(world.AccountId, nzd, 4000.01m, expectedRate: 2.00000000m));
        var accepted = await FxConversionTests.PostAsync(
            world.TenantAdmin, FxConversionTests.SellBody(world.AccountId, nzd, 4000.00m, expectedRate: 2.00000000m));

        (await CustodialHttp.ErrorsAsync(quoteRefused))[""].ShouldBe([LeavingRefusal]);
        quoteAccepted.StatusCode.ShouldBe(HttpStatusCode.OK, await quoteAccepted.Content.ReadAsStringAsync());
        (await CustodialHttp.ErrorsAsync(refused))[""].ShouldBe([LeavingRefusal]);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AnAccountWithALiveOrder_CannotBeClosed()
    {
        var world = await OrderWorld.CreateAsync();
        await world.PlaceAsync("Buy", 1m);

        var response = await CustodialHttp.CloseAsync(world.TenantAdmin, world.AccountId);

        (await CustodialHttp.ErrorsAsync(response))["AccountId"]
            .ShouldBe(["Open orders must be filled, cancelled or expired before the account can be closed."]);
    }

    [Test]
    public async Task AnAccountWithAFillThatHasNotSettled_CannotBeClosed_AndAfterItSettlesItCan()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 10m);
        var id = await world.PlaceAsync("Sell", 10m);
        await BrokerHttp.SendAsync();
        var fill = Feed.AddFill(id, 10m, 99m);
        await BrokerHttp.RunReportsAsync();

        var refused = await CustodialHttp.CloseAsync(world.TenantAdmin, world.AccountId);

        (await CustodialHttp.ErrorsAsync(refused))["AccountId"].ShouldBe(["Trades must settle before the account can be closed."]);

        Feed.AddSettled(id, fill.Id!);
        await BrokerHttp.RunReportsAsync();

        (await CustodialHttp.CloseAsync(world.TenantAdmin, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
