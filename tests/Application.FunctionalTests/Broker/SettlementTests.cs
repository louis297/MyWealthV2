using System.Net;
using System.Text.Json;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R42–R44: a fill settles when the broker says so; the proceeds of an unsettled sale are in the
/// balance but are not withdrawable.</summary>
[Parallelizable]
public class SettlementTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    /// <summary>40 ACME bought by hand at 25.00, then a sell order of 10 filled at 99.00: 985.00 net after the fee.</summary>
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

    private static async Task<JsonElement> ReadAsync(string accessToken, string path)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    private static (decimal Cash, decimal Reserved, decimal Available, decimal Unsettled, decimal Withdrawable) Figures(JsonElement item) =>
        (item.GetProperty("cashBalance").GetDecimal(), item.GetProperty("reservedCash").GetDecimal(),
            item.GetProperty("availableCash").GetDecimal(), item.GetProperty("unsettledCash").GetDecimal(),
            item.GetProperty("withdrawableCash").GetDecimal());

    [Test]
    public async Task ThePostedProceedsOfASale_AreInTheBalance_ButNotWithdrawable_OnEveryAccountRead()
    {
        var (world, _, _) = await SoldAsync();

        foreach (var token in new[] { world.TenantAdmin, world.Adviser, world.CustomerToken })
        {
            Figures(await ReadAsync(token, $"/accounts/{world.AccountId}")).ShouldBe((4985.00m, 0m, 4985.00m, 985.00m, 4000.00m));
            Figures(await ReadAsync(token, $"/accounts/{world.AccountId}/cash-balance")).ShouldBe((4985.00m, 0m, 4985.00m, 985.00m, 4000.00m));
        }

        var listed = (await ReadAsync(world.TenantAdmin, "/accounts")).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == world.AccountId);
        Figures(listed).ShouldBe((4985.00m, 0m, 4985.00m, 985.00m, 4000.00m));
    }

    [Test]
    public async Task ASettledReport_SettlesTheFill_AndItsCashBecomesWithdrawable()
    {
        var (world, id, fillReport) = await SoldAsync();
        var settledDate = OrderHttp.Today.AddDays(2);
        var sent = Feed.AddSettled(id, fillReport, settledDate);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((1, 0, 0));
        (await BrokerHttp.ReportAsync(sent.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        var fill = (await OrderHttp.OrderAsync(id)).Fills.Single();
        fill.SettledAt!.Value.ShouldBeGreaterThan(before);
        fill.SettledDate.ShouldBe(settledDate);
        var listed = (await OrderHttp.GetAsync(world.CustomerToken, id)).GetProperty("fills").EnumerateArray().Single();
        listed.GetProperty("settledAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        using var transaction = await CustodialHttp.GetTransactionAsync(world.CustomerToken, listed.GetProperty("transactionId").GetGuid());
        transaction.RootElement.GetProperty("execution").GetProperty("settledAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{world.AccountId}")).ShouldBe((4985.00m, 0m, 4985.00m, 0m, 4985.00m));
        // Settlement writes nothing to the ledger.
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(3);
    }

    [Test]
    public async Task ASettledReport_ForAFillTheOrderDoesNotHave_IsSkippedAsUnknownFill_AndASecondOneAsRepeated()
    {
        var (world, id, fillReport) = await SoldAsync();
        var unknown = Feed.AddSettled(id, "fake-X-none");
        var first = Feed.AddSettled(id, fillReport);
        var second = Feed.AddSettled(id, fillReport);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((1, 2, 0));
        (await BrokerHttp.ReportAsync(unknown.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.UnknownFill);
        (await BrokerHttp.ReportAsync(first.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        (await BrokerHttp.ReportAsync(second.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.Repeated);
        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{world.AccountId}")).Unsettled.ShouldBe(0m);
    }

    [Test]
    public async Task ABuyFill_AddsNothingToUnsettledCash_AndIsNotNettedAgainstASale()
    {
        var (world, _, _) = await SoldAsync();
        var buy = await world.PlaceAsync("Buy", 2m);
        await BrokerHttp.SendAsync();
        Feed.AddFill(buy, 2m, 100m);

        await BrokerHttp.RunReportsAsync();

        // 4 985.00 less 200.00 and the 5.00 minimum fee; the sale's 985.00 is still unsettled.
        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{world.AccountId}")).ShouldBe((4780.00m, 0m, 4780.00m, 985.00m, 3795.00m));
    }

    [Test]
    public async Task WithdrawableCash_IsNeverBelowZero()
    {
        var (world, _, _) = await SoldAsync();
        await world.PlaceAsync("Buy", 40m);

        // The buy order reserves 4 221.00; 764.00 is available, less than the 985.00 that has not settled.
        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{world.AccountId}")).ShouldBe((4985.00m, 4221.00m, 764.00m, 985.00m, 0m));
    }

    [Test]
    public async Task AFill_CanSettleWhileItWaitsToBePosted_AndThenIsNotUnsettled()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var id = await world.PlaceAsync("Sell", 10m);
        await BrokerHttp.SendAsync();
        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Closed);
        var fill = Feed.AddFill(id, 10m, 99m);
        Feed.AddSettled(id, fill.Id!);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped).ShouldBe((2, 0));
        var waiting = (await OrderHttp.OrderAsync(id)).Fills.Single();
        (waiting.Status, waiting.IsSettled).ShouldBe((OrderFillStatus.Pending, true));

        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Open);
        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(1, 0, 0));
        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{world.AccountId}")).ShouldBe((4985.00m, 0m, 4985.00m, 0m, 4985.00m));
    }

    [Test]
    public async Task ATradeThatIsNotAFill_CountsAsSettled_AndAnExternalAccountHasNoUnsettledCash()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.TenantAdmin, external, 250m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{world.AccountId}")).ShouldBe((4000.00m, 0m, 4000.00m, 0m, 4000.00m));
        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{external}")).ShouldBe((250m, 0m, 250m, 0m, 250m));
        Figures(await ReadAsync(world.TenantAdmin, $"/accounts/{external}/cash-balance")).ShouldBe((250m, 0m, 250m, 0m, 250m));
    }
}
