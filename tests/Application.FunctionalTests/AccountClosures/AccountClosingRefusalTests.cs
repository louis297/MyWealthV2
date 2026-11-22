using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>Account closure R11–R13: a closing account accepts no hand write and no portfolio assignment.</summary>
[Parallelizable]
public class AccountClosingRefusalTests : TestBase
{
    private const string ClosingMessage = "This account is being closed and accepts no new activity.";
    private const string AssignMessage = "Only an open Custodial account that is not being closed can be assigned a portfolio.";
    private const string TransferPath = "/transactions/transfer";
    private const string FxPath = "/transactions/fx";
    private const string QuotePath = "/transactions/fx/quote";
    private const decimal NzdUsd = 0.59120000m;

    [Test]
    public async Task HandWrites_OnTheClosingAccount_Return400_ForEveryCallerAllowedThem_AndWriteNothing()
    {
        var closing = await ClosingAsync();
        var world = closing.World;
        var system = await AccountHttp.SignInSystemAdmin();
        var instrument = await CreateInstrumentAsync(world.TenantAdmin, "VTI", "USD", 25m);
        var writes = new List<(string Name, string Token, string Path, Dictionary<string, object?> Body)>();
        foreach (var (role, token) in new[] { ("TenantAdmin", world.TenantAdmin), ("Adviser", world.Adviser), ("SystemAdmin", system.AccessToken) })
        {
            var buy = Trade(world.AccountId, "Buy", instrument, 1m);
            var closeOut = Cash(world.AccountId, "CloseOut", -closing.Cash);
            if (role == "SystemAdmin")
            {
                // A SystemAdmin names the tenant of the account (posting R2).
                buy["tenantId"] = world.Tenant.PublicId;
                closeOut["tenantId"] = world.Tenant.PublicId;
            }

            writes.Add(($"{role} buy", token, "/transactions", buy));
            writes.Add(($"{role} close out", token, "/transactions", closeOut));
        }

        foreach (var (role, token) in new[] { ("TenantAdmin", world.TenantAdmin), ("Adviser", world.Adviser) })
        {
            writes.Add(($"{role} transfer out", token, TransferPath, Transfer(world.AccountId, closing.Spare)));
            writes.Add(($"{role} transfer in", token, TransferPath, Transfer(closing.Spare, world.AccountId)));
            writes.Add(($"{role} convert out", token, FxPath, Conversion(world.AccountId, closing.Nzd)));
            writes.Add(($"{role} convert in", token, FxPath, Conversion(closing.Nzd, world.AccountId)));
        }

        writes.Add(("TenantAdmin adjustment", world.TenantAdmin, AdjustmentWorld.Path, AdjustmentWorld.Body(world.AccountId, amount: 5m)));
        var before = await TransactionCountAsync(world.AccountId);

        foreach (var (name, token, path, body) in writes)
        {
            var key = Guid.NewGuid();
            var response = await PostAsync(token, body, path, key);

            ShouldBeClosing(await ErrorsAsync(response), name);
            (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse(name);
        }

        (await TransactionCountAsync(world.AccountId)).ShouldBe(before);
        (await TransactionCountAsync(closing.Spare)).ShouldBe(1);
        (await TransactionCountAsync(closing.Nzd)).ShouldBe(1);
    }

    [Test]
    public async Task AQuote_WithTheClosingAccountOnEitherSide_Returns400()
    {
        var closing = await ClosingAsync();

        foreach (var (from, to) in new[] { (closing.World.AccountId, closing.Nzd), (closing.Nzd, closing.World.AccountId) })
        {
            var quote = new Dictionary<string, object?> { ["fromAccountId"] = from, ["toAccountId"] = to, ["sellAmount"] = 10m };
            ShouldBeClosing(await ErrorsAsync(await TransactionHttp.Send(HttpMethod.Post, QuotePath, closing.World.Adviser, quote)));
        }
    }

    [Test]
    public async Task AReverse_OfAHandTransferInOnTheClosingAccount_Returns400()
    {
        var closing = await ClosingAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { closing.World.TenantAdmin, closing.World.Adviser, system.AccessToken })
        {
            ShouldBeClosing(await ErrorsAsync(await ReverseAsync(token, closing.HandTransferIn)));
        }

        (await AccountClosureHttp.TransactionAsync(closing.HandTransferIn)).ReversedByTransactionId.ShouldBeNull();
    }

    [Test]
    public async Task AWithdrawal_FromTheClosingAccount_Returns400()
    {
        var closing = await ClosingAsync();
        var world = closing.World;

        foreach (var token in new[] { world.TenantAdmin, world.Adviser })
        {
            var key = Guid.NewGuid();
            var response = await WithdrawalHttp.PostAsync(token, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 0.01m), key);

            ShouldBeClosing(await ErrorsAsync(response));
            (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
        }

        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task AMatchToTheClosingAccount_AndAnUnmatchOfADepositOnIt_Return400()
    {
        var closing = await ClosingAsync();
        var world = closing.World;
        var receipt = BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 25m, "USD", reference: null);
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(receipt.Id!);
        var deposit = await DepositLineAsync(world.AccountId);

        var match = await StatementLineHttp.ActAsync(world.TenantAdmin, line.PublicId, "match",
            new Dictionary<string, object?> { ["accountId"] = world.AccountId });
        var unmatch = await StatementLineHttp.ActAsync(world.TenantAdmin, deposit, "unmatch",
            new Dictionary<string, object?> { ["note"] = "Wrong account" });

        ShouldBeClosing(await ErrorsAsync(match));
        ShouldBeClosing(await ErrorsAsync(unmatch));
        (await BankFeedHttp.LineAsync(receipt.Id!)).MatchedAccountId.ShouldBeNull();
    }

    [Test]
    public async Task AssigningAPortfolio_ToTheClosingAccount_Returns400AccountNotEligible()
    {
        var closing = await ClosingAsync();
        var portfolio = await AccountClosureHttp.CreatePortfolioAsync(closing.World.TenantAdmin);

        var response = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{closing.World.AccountId}/portfolio", closing.World.Adviser, new { portfolioId = portfolio });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var problem = await ProblemResponse.ReadAsync(response);
        problem.Code.ShouldBe("account_not_eligible");
        problem.Detail.ShouldBe(AssignMessage);
    }

    [Test]
    public async Task TheSameWrites_OnAnotherOpenCustodialAccountOfTheCustomer_Succeed()
    {
        var closing = await ClosingAsync();
        var world = closing.World;
        var instrument = await CreateInstrumentAsync(world.TenantAdmin, "VTI", "USD", 25m);

        (await TradeAsync(closing.Spare, "Buy", instrument, 1m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(world.Adviser, Conversion(closing.Nzd, closing.Spare), FxPath)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(closing.Spare, world.NominatedBankAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(world.TenantAdmin, AdjustmentWorld.Body(closing.Spare, amount: 1m), AdjustmentWorld.Path))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static void ShouldBeClosing(Dictionary<string, string[]> errors, string? because = null)
    {
        errors.Keys.ToArray().ShouldBe([string.Empty], customMessage: because);
        errors[string.Empty].ShouldBe([ClosingMessage], customMessage: because);
    }

    private static Dictionary<string, object?> Transfer(Guid from, Guid to) => new()
    {
        ["fromAccountId"] = from,
        ["toAccountId"] = to,
        ["amount"] = 10m
    };

    private static Dictionary<string, object?> Conversion(Guid from, Guid to) => new()
    {
        ["fromAccountId"] = from,
        ["toAccountId"] = to,
        ["sellAmount"] = 10m,
        ["expectedRate"] = NzdUsd
    };

    private static async Task<Guid> DepositLineAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        return await db.BankStatementLines.IgnoreQueryFilters()
            .Where(row => row.MatchedAccountId == account.Id)
            .Select(row => row.PublicId)
            .FirstAsync();
    }

    /// <summary>
    /// Ned's USD account (1 000.00 deposited from the bank and a hand <c>TransferIn</c> of 20.00) is closing: Ann
    /// requested its closure, which waits for approval. Ned also has an Open Custodial USD account (Spare, 100.00) and
    /// an Open Custodial NZD account (1 000.00), with an NZD/USD rate.
    /// </summary>
    private static async Task<ClosingWorld> ClosingAsync()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var handTransferIn = await InsertManualTransferInAsync(world.AccountId, 20m);
        var spare = await OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Spare", "USD");
        (await FundAsync(world.TenantAdmin, spare, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await FxConversionTests.SetRateAsync("NZD", "USD", NzdUsd, DateOnly.FromDateTime(DateTime.UtcNow));
        var nzd = await OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "NZD side", "NZD");
        (await FundAsync(world.TenantAdmin, nzd, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);
        return new ClosingWorld(world, spare, nzd, handTransferIn, 1020m);
    }

    private sealed record ClosingWorld(WithdrawalWorld World, Guid Spare, Guid Nzd, Guid HandTransferIn, decimal Cash);
}
