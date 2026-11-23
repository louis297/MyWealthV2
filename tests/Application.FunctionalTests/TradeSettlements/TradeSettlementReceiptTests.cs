using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R19: the broker's receipt names the settlement it pays, and a receipt that does not
/// match it goes to suspense.</summary>
[Parallelizable]
public class TradeSettlementReceiptTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    [Test]
    public async Task TheCreditLineNamingTheSettlement_WithItsNet_MatchesAsSettlement_AndCompletesIt_WithNoTransaction()
    {
        var (world, clientMoney, settlement) = await ReceivingAsync();
        var cashBefore = await world.CashAsync();
        var credit = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 990m, "USD", "w-S2", "SIMULATOR BROKER", "BRK-SIM-USD");

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(credit.Id!);
        (line.Status, line.MatchMethod, line.TradeSettlementId, line.MatchedAccountId, line.MatchedTransactionId).ShouldBe((
            BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.Settlement, (int?)settlement.Id,
            (int?)null, (int?)null));
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        var completed = await TradeSettlementPaymentTests.SettlementAsync(settlement.Id);
        (completed.Status, completed.CompletedAt is not null).ShouldBe((TradeSettlementStatus.Completed, true));
        (await world.CashAsync()).ShouldBe(cashBefore);
    }

    [Test]
    public async Task TheLineItem_NamesItsTradeSettlement_AndTheLineCannotBeUnmatched()
    {
        var (world, clientMoney, settlement) = await ReceivingAsync();
        var credit = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 990m, "USD", "w-S2");
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(credit.Id!);

        var item = await StatementLines.StatementLineHttp.GetAsync(world.TenantAdmin, line.PublicId);
        var unmatch = await StatementLines.StatementLineHttp.ActAsync(world.TenantAdmin, line.PublicId, "unmatch", new() { ["note"] = "Wrong" });

        item.TryGetProperty("tradeSettlementId", out var named).ShouldBeTrue();
        named.GetGuid().ShouldBe(settlement.PublicId);
        item.GetProperty("match").GetProperty("method").GetString().ShouldBe("Settlement");
        (await Custody.CustodialHttp.ErrorsAsync(unmatch))["Status"]
            .ShouldBe(["This line is the cash of a trade settlement and cannot be unmatched."]);
        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.Completed);
    }

    [Test]
    public async Task TheSettlementIdIsComparedTrimmed_AndWithoutRegardToCase()
    {
        var (_, clientMoney, settlement) = await ReceivingAsync();
        BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 990m, "USD", "  W-s2 ");

        await BankFeedHttp.RunAsync();

        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.Completed);
    }

    [Test]
    public async Task AReceiptOfAnotherAmount_IsASettlementMismatch_AndTheSettlementDoesNotChange()
    {
        var (_, clientMoney, settlement) = await ReceivingAsync();
        var credit = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 989.99m, "USD", "w-S2");

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(credit.Id!);
        (line.Status, line.UnmatchedReason, line.TradeSettlementId).ShouldBe((
            BankStatementLineStatus.Unmatched, (UnmatchedReason?)UnmatchedReason.SettlementMismatch, (int?)null));
        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.AwaitingReceipt);
    }

    [Test]
    public async Task ASecondReceiptForACompletedSettlement_IsASettlementMismatch()
    {
        var (_, clientMoney, _) = await ReceivingAsync();
        BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 990m, "USD", "w-S2");
        await BankFeedHttp.RunAsync();
        var second = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 990m, "USD", "w-S2");

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(second.Id!)).UnmatchedReason.ShouldBe(UnmatchedReason.SettlementMismatch);
    }

    [Test]
    public async Task AReceiptNamingASettlementId_ThatIsAlsoADepositReference_IsNeverMatchedAsADeposit()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        var reference = await BankFeedHttp.DepositReferenceAsync(world.AccountId);
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var (id, fill) = await TradeSettlementReportsTests.FilledAsync(world, "Sell", 10m, 99m);
        Feed.AddSettled(id, fill, settlementId: reference);
        Feed.AddSettlement(reference, clientMoney.AccountNumber, 990m, 1);
        await BrokerHttp.RunReportsAsync();
        var cashBefore = await world.CashAsync();
        var credit = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 500m, "USD", reference);

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(credit.Id!);
        (line.Status, line.UnmatchedReason, line.MatchedAccountId)
            .ShouldBe((BankStatementLineStatus.Unmatched, (UnmatchedReason?)UnmatchedReason.SettlementMismatch, (int?)null));
        (await world.CashAsync()).ShouldBe(cashBefore);
    }

    [Test]
    public async Task AReceiptOfAnotherTenantsSettlementId_IsNotSettlementCash()
    {
        var (_, _, _) = await ReceivingAsync();
        var (south, southMoney) = await TradeSettlementReportsTests.WorldAsync("South Advisory", "south-advisory");
        var credit = BankFeedHttp.Feed.AddReceipt(southMoney.AccountNumber, 990m, "USD", "w-S2");

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(credit.Id!)).UnmatchedReason.ShouldNotBe(UnmatchedReason.SettlementMismatch);
        (await TradeSettlementReportsTests.SettlementsAsync(south.Tenant.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task AnInterestLineWithTheSettlementIdAsReference_StaysBankInterest()
    {
        var (_, clientMoney, settlement) = await ReceivingAsync();
        var interest = BankFeedHttp.Feed.Add(clientMoney.AccountNumber, new BankFeedLine(
            "fake-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Credit, 990m, "USD",
            "SIMULATOR BANK", null, "w-S2", Kind: StatementLineKind.Interest));

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(interest.Id!)).Status.ShouldBe(BankStatementLineStatus.Ignored);
        (await TradeSettlementPaymentTests.SettlementAsync(settlement.Id)).Status.ShouldBe(TradeSettlementStatus.AwaitingReceipt);
    }

    /// <summary>A sell of 10 at 99.00 settled in <c>w-S2</c>: the broker owes 990.00.</summary>
    internal static async Task<(OrderWorld World, ClientMoneyAccount ClientMoney, TradeSettlement Settlement)> ReceivingAsync()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var (id, fill) = await TradeSettlementReportsTests.FilledAsync(world, "Sell", 10m, 99m);
        Feed.AddSettled(id, fill, settlementId: "w-S2");
        Feed.AddSettlement("w-S2", clientMoney.AccountNumber, 990m, 1);
        await BrokerHttp.RunReportsAsync();
        var settlement = (await TradeSettlementReportsTests.SettlementsAsync(world.Tenant.Id)).Single();
        settlement.Status.ShouldBe(TradeSettlementStatus.AwaitingReceipt);
        return (world, clientMoney, settlement);
    }
}
