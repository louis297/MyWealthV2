using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Withdrawals R36, R37: every write that lowers cash on a Custodial account keeps available cash at or
/// above zero. Each world has 1 000.00 USD with 300.00 reserved, so 700.00 is available.</summary>
[Parallelizable]
public class CustodialAvailableCashTests : TestBase
{
    private const string Refusal = "Available cash cannot become negative: 300.00 USD is reserved for withdrawals and orders.";

    [Test]
    public async Task InternalTransfer_FromIt_AboveTheAvailableCash_Returns400()
    {
        var world = await ReservedWorldAsync();
        var second = await OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Second");

        (await ErrorsAsync(await PostAsync(world.TenantAdmin, Transfer(world.AccountId, second, 700.01m), "/transactions/transfer")))[""]
            .ShouldBe([Refusal]);
        (await PostAsync(world.TenantAdmin, Transfer(world.AccountId, second, 700m), "/transactions/transfer"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task FxConversion_FromIt_AboveTheAvailableCash_Returns400()
    {
        var world = await ReservedWorldAsync();
        var nzd = await OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Kiwi", "NZD");
        await FxConversionTests.SetRateAsync("USD", "NZD", 2.00000000m, FxConversionTests.RateAsOf);

        (await ErrorsAsync(await FxConversionTests.PostAsync(world.TenantAdmin,
            FxConversionTests.SellBody(world.AccountId, nzd, 700.01m, expectedRate: 2.00000000m))))[""].ShouldBe([Refusal]);
        (await FxConversionTests.PostAsync(world.TenantAdmin, FxConversionTests.SellBody(world.AccountId, nzd, 700m, expectedRate: 2.00000000m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task NegativeAdjustment_AboveTheAvailableCash_Returns400()
    {
        var world = await ReservedWorldAsync();

        (await ErrorsAsync(await Transactions.AdjustmentWorld.PostAsync(world.TenantAdmin, Adjustment(world.AccountId, -700.01m))))
            .Values.SelectMany(messages => messages).ShouldContain(Refusal);
        (await Transactions.AdjustmentWorld.PostAsync(world.TenantAdmin, Adjustment(world.AccountId, -700m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task HandReverseOfASell_BelowTheReservation_Returns400()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var instrument = await CreateInstrumentAsync(world.TenantAdmin, price: 25.00m);
        // Trades typed before orders (orders R48): a person can still reverse them.
        await InsertManualTradeAsync(world.AccountId, "Buy", instrument, 28m);
        var sell = await InsertManualTradeAsync(world.AccountId, "Sell", instrument, 28m);
        await world.RequestAsync(400m);

        (await ErrorsAsync(await ReverseAsync(world.TenantAdmin, sell)))[""]
            .ShouldBe(["Available cash cannot become negative: 400.00 USD is reserved for withdrawals and orders."]);
        (await world.CashAsync()).ShouldBe((1000m, 400m, 600m));
    }

    [Test]
    public async Task UndoMatch_BelowTheReservation_Returns400()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 200m, "USD", world.DepositReference);
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(sent.Id!);
        await world.RequestAsync(1100m);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin, line.PublicId, "unmatch", new() { ["note"] = "Wrong account" });

        (await ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("Available cash cannot become negative: 1100.00 USD is reserved for withdrawals and orders.");
        (await world.CashAsync()).ShouldBe((1200m, 1100m, 100m));
    }

    [Test]
    public async Task Rebalance_InvestsAtMostTheAvailableCash()
    {
        var world = await ReservedWorldAsync();
        var instrument = await CreateInstrumentAsync(world.TenantAdmin, price: 10.00m);
        var portfolio = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", world.TenantAdmin, new
        {
            name = "All in",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = instrument, targetWeight = 100m } }
        });
        var portfolioId = await ReadIdAsync(portfolio);

        var assign = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/portfolio", world.TenantAdmin, new { portfolioId });

        assign.StatusCode.ShouldBe(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        // Rebalances R16: each buy wave is sized so its reservations fit the available cash; the withdrawal's 300.00
        // is never invested. What a top-up cannot buy at its price limit stays as cash.
        using var json = System.Text.Json.JsonDocument.Parse(await assign.Content.ReadAsStringAsync());
        await Rebalances.RebalanceHttp.CompleteAsync(world.TenantAdmin, json.RootElement.GetProperty("rebalanceId").GetGuid());
        var (cash, reserved, available) = await world.CashAsync();
        reserved.ShouldBe(300m);
        cash.ShouldBeGreaterThanOrEqualTo(300m);
        cash.ShouldBeLessThan(305m);
        available.ShouldBeGreaterThanOrEqualTo(0m);
    }

    private static async Task<WithdrawalWorld> ReservedWorldAsync()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await world.RequestAsync(300m);
        (await world.CashAsync()).ShouldBe((1000m, 300m, 700m));
        return world;
    }

    private static Dictionary<string, object?> Transfer(Guid from, Guid to, decimal amount) => new()
    {
        ["fromAccountId"] = from,
        ["toAccountId"] = to,
        ["amount"] = amount
    };

    private static Dictionary<string, object?> Adjustment(Guid accountId, decimal amount) => new()
    {
        ["accountId"] = accountId,
        ["amount"] = amount,
        ["reason"] = "EntryError",
        ["memo"] = "Correction"
    };
}
