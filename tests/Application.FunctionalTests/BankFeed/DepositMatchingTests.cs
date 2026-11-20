using System.Net;
using System.Text.Json;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>Deposit matching R13, R15–R20, R32, R36: processing stored receipts into deposits or suspense.</summary>
[Parallelizable]
public class DepositMatchingTests : TestBase
{
    [Test]
    public async Task ReceiptWithTheReference_PostsADeposit_AndTheLineIsMatchedByReference()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var spaced = $"{world.DepositReference[..3]} {world.DepositReference[3..6]} {world.DepositReference[6..]}";

        var line = await world.ReceiveAsync(500.00m, spaced);

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Reference);
        line.UnmatchedReason.ShouldBeNull();
        line.LastAction.ShouldBeNull();
        var deposit = (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldHaveSingleItem();
        line.MatchedTransactionId.ShouldBe(deposit.Id);
        deposit.Type.ShouldBe(TransactionType.TransferIn);
        deposit.Memo.ShouldBe("Bank deposit");
        deposit.Reference.ShouldBeNull();
        deposit.CreatedBy.ShouldBe(SystemActor.Name);
        deposit.BookedAt.ShouldBeGreaterThan(before);
        var leg = deposit.CashLegs.ShouldHaveSingleItem();
        leg.Amount.ShouldBe(500.00m);
        leg.Currency.ShouldBe("USD");
        (await world.CashBalanceAsync()).ShouldBe(500.00m);

        using var item = await CustodialHttp.GetTransactionAsync(world.TenantAdmin.AccessToken, deposit.PublicId);
        var root = item.RootElement;
        root.GetProperty("type").GetString().ShouldBe("TransferIn");
        root.GetProperty("memo").GetString().ShouldBe("Bank deposit");
        root.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("BankFeed");
        root.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(line.PublicId);
        var receipt = root.GetProperty("bankReceipt");
        receipt.GetProperty("valueDate").GetString().ShouldBe(line.ValueDate.ToString("yyyy-MM-dd"));
        receipt.GetProperty("reference").GetString().ShouldBe(spaced);
        receipt.GetProperty("payerName").GetString().ShouldBe("J SMITH");
        receipt.GetProperty("payerAccount").GetString().ShouldBe("****4200");
    }

    [Test]
    public async Task CustomerAndAdviser_ReadTheDepositWithItsBankReceipt_AndOtherTransactionsHaveNone()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(25m, world.DepositReference);
        var deposit = (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).Single();
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);
        var adviser = await world.SignInAdviserAsync();

        foreach (var token in new[] { customer.AccessToken, adviser.AccessToken })
        {
            var list = await AccountHttp.Send(HttpMethod.Get, $"/transactions?accountId={world.AccountId}", token);
            var content = await list.Content.ReadAsStringAsync();
            list.StatusCode.ShouldBe(HttpStatusCode.OK, content);
            using var json = JsonDocument.Parse(content);
            var row = json.RootElement.GetProperty("items").EnumerateArray()
                .Single(entry => entry.GetProperty("id").GetGuid() == deposit.PublicId);
            row.GetProperty("bankReceipt").GetProperty("payerAccount").GetString().ShouldBe("****4200");
        }

        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);
        var transfer = await CustodialHttp.ReadIdAsync(
            await CustodialHttp.FundAsync(world.TenantAdmin.AccessToken, external, 5m, external: true));
        using var other = await CustodialHttp.GetTransactionAsync(world.TenantAdmin.AccessToken, transfer);
        other.RootElement.GetProperty("bankReceipt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [TestCase("Rent for November", UnmatchedReason.NoReference)]
    [TestCase(null, UnmatchedReason.NoReference)]
    [TestCase("H2N4R6T80", UnmatchedReason.UnknownReference)]
    public async Task ReceiptWithoutAKnownReference_IsUnmatched_AndNothingIsPosted(string? reference, UnmatchedReason reason)
    {
        var world = await DepositMatchingWorld.CreateAsync();

        var line = await world.ReceiveAsync(10m, reference);

        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(reason);
        line.MatchedAccountId.ShouldBeNull();
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task AnotherTenantsReference_IsUnknown()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var north = await DepositMatchingWorld.CreateAsync();

        var line = await north.ReceiveAsync(10m, south.DepositReference);

        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReference);
        (await south.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task TwoReferencesOfTwoAccounts_AreSeveralReferences()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var second = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Second");
        var secondReference = await BankFeedHttp.DepositReferenceAsync(second);

        var line = await world.ReceiveAsync(10m, $"{world.DepositReference} {secondReference}");

        line.UnmatchedReason.ShouldBe(UnmatchedReason.SeveralReferences);
    }

    [Test]
    public async Task TheSameReferenceTwice_IsOneAccount()
    {
        var world = await DepositMatchingWorld.CreateAsync();

        var line = await world.ReceiveAsync(10m, $"{world.DepositReference} / {world.DepositReference.ToLowerInvariant()}");

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task AccountInAnotherCurrency_IsCurrencyMismatch_BeforeItIsClosed()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var nzd = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Kiwi", "NZD");
        (await CustodialHttp.CloseAsync(world.TenantAdmin.AccessToken, nzd)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var line = await world.ReceiveAsync(10m, await BankFeedHttp.DepositReferenceAsync(nzd));

        line.UnmatchedReason.ShouldBe(UnmatchedReason.CurrencyMismatch);
    }

    [Test]
    public async Task ClosedAccount_IsAccountClosed_BeforeADisabledCustomer()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        (await CustodialHttp.CloseAsync(world.TenantAdmin.AccessToken, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var line = await world.ReceiveAsync(10m, world.DepositReference);

        line.UnmatchedReason.ShouldBe(UnmatchedReason.AccountClosed);
    }

    [Test]
    public async Task DisabledCustomer_IsCustomerDisabled_BeforeTheAmountPrecision()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var line = await world.ReceiveAsync(10.005m, world.DepositReference);

        line.UnmatchedReason.ShouldBe(UnmatchedReason.CustomerDisabled);
    }

    [Test]
    public async Task AmountWithMoreDecimalsThanTheCurrency_IsAmountPrecision()
    {
        var world = await DepositMatchingWorld.CreateAsync();

        var line = await world.ReceiveAsync(10.005m, world.DepositReference);

        line.UnmatchedReason.ShouldBe(UnmatchedReason.AmountPrecision);
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task LaterChange_DoesNotMatchTheLineAgain()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);
        var line = await world.ReceiveAsync(10m, world.DepositReference);
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: false);

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task ProcessingTwice_PostsOneDeposit()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(10m, world.DepositReference);

        await BankFeedHttp.ProcessAsync();
        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldHaveSingleItem();
        (await world.CashBalanceAsync()).ShouldBe(10m);
    }

    [Test]
    public async Task LinesStoredEarlier_AreProcessed_WhenTheReadFails()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 10m, "USD", world.DepositReference);
        await BankFeedHttp.PullAsync();
        BankFeedHttp.Feed.Fail(world.ClientMoney.AccountNumber, BankFeedFailure.Unavailable);

        var outcome = await BankFeedHttp.RunAsync();

        outcome.AnyFailed.ShouldBeTrue();
        (await BankFeedHttp.LineAsync(sent.Id!)).Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task LinesAreProcessedOldestFirst()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var first = BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 1m, "USD", world.DepositReference);
        await BankFeedHttp.PullAsync();
        var second = BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 2m, "USD", world.DepositReference);
        await BankFeedHttp.PullAsync();

        await BankFeedHttp.ProcessAsync();

        var firstDeposit = (await BankFeedHttp.TransactionsOfLineAsync((await BankFeedHttp.LineAsync(first.Id!)).PublicId)).Single();
        var secondDeposit = (await BankFeedHttp.TransactionsOfLineAsync((await BankFeedHttp.LineAsync(second.Id!)).PublicId)).Single();
        firstDeposit.Id.ShouldBeLessThan(secondDeposit.Id);
    }

    [Test]
    public async Task DisabledTenant_IsMatchedAsUsual()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        var line = await world.ReceiveAsync(10m, world.DepositReference);

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task DebitLine_IsNotProcessed()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.AddReceipt(
            world.ClientMoney.AccountNumber, 10m, "USD", world.DepositReference, direction: BankLineDirection.Debit);

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(sent.Id!)).Status.ShouldBe(BankStatementLineStatus.Received);
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task DepositOnAnAccountWithAnActivePortfolio_IsAllowed_AndEnqueuesNoRebalance()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var instrument = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin.AccessToken, "AIA", "USD", 10m);
        var portfolio = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", world.TenantAdmin.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = instrument, targetWeight = 100m } }
        });
        var portfolioId = await CustodialHttp.ReadIdAsync(portfolio);
        var assign = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{world.AccountId}/portfolio", world.TenantAdmin.AccessToken, new { portfolioId });
        assign.StatusCode.ShouldBe(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();
        var enqueued = scheduler.Enqueued.Count;

        var line = await world.ReceiveAsync(100m, world.DepositReference);

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        (await world.CashBalanceAsync()).ShouldBe(100m);
        scheduler.Enqueued.Count.ShouldBe(enqueued);
    }
}
