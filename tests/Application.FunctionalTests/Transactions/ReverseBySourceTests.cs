using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Transaction source R12–R16: on a Custodial account a person reverses only a hand-reversible source.</summary>
[Parallelizable]
public class ReverseBySourceTests : TestBase
{
    private const string Message = "This transaction was written by the platform and cannot be reversed by hand.";
    private const string ClosedMessage = "Cannot reverse a transaction while the account is closed. Reopen the account first.";
    private const string TradeLockMessage = "This account is assigned to a portfolio. Manual security trades are not allowed.";

    [Test]
    public async Task Custodial_ManualTransferIn_Returns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        var transferIn = await InsertManualTransferInAsync(accountId, 100m);

        var response = await ReverseAsync(tokens.AccessToken, transferIn);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Custodial_RebalanceTradeAfterUnassign_Returns400_AndWritesNothing()
    {
        var world = await RebalanceTrades.AssignedAsync();
        (await FundAsync(world.AccessToken, world.AccountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        // A rebalance trade posted before rebalances went through orders (rebalances R26).
        var trade = await InsertRebalanceTradeAsync(world.AccountId, world.Second, 1m, 20m);
        (await PortfolioHttp.Send(HttpMethod.Delete, $"/accounts/{world.AccountId}/portfolio", world.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var before = await TransactionCountAsync(world.AccountId);

        var response = await ReverseAsync(world.AccessToken, trade);

        // Rebalances R28: it is corrected with an adjustment, not reversed by hand.
        (await ErrorsAsync(response)).Values.SelectMany(messages => messages).ShouldBe([Message]);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(before);
    }

    [Test]
    public async Task External_RebalanceTrade_Returns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, currency: "NZD");
        (await FundAsync(tokens.AccessToken, accountId, 1000m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA", "NZD", 10m);
        var trade = await InsertRebalanceTradeAsync(accountId, instrumentId, 1m, 10m);

        var response = await ReverseAsync(tokens.AccessToken, trade);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Custodial_SeedTransaction_Returns400AndWritesNothing_EvenOnRetry()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        var seeded = await InsertSeedTransferInAsync(tenant.Id, accountId, 100m);
        var key = Guid.NewGuid();

        var first = await ReverseWithKeyAsync(tokens.AccessToken, seeded, key);
        var retry = await ReverseWithKeyAsync(tokens.AccessToken, seeded, key);

        (await ErrorsAsync(first)).Values.SelectMany(messages => messages).ShouldBe([Message]);
        (await ErrorsAsync(retry)).Values.SelectMany(messages => messages).ShouldBe([Message]);
        (await TransactionCountAsync(accountId)).ShouldBe(1);
        (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
        using var item = await GetTransactionAsync(tokens.AccessToken, seeded);
        item.RootElement.GetProperty("reversedByTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task External_SeedTransaction_Returns201()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, currency: "NZD");
        var seeded = await InsertSeedTransferInAsync(tenant.Id, accountId, 100m);

        var response = await ReverseAsync(tokens.AccessToken, seeded);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Custodial_SeedTransactionOnAClosedAccount_ReturnsTheClosedRefusal()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        (await CloseAsync(tokens.AccessToken, accountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var seeded = await InsertSeedTransferInAsync(tenant.Id, accountId, 100m);

        var response = await ReverseAsync(tokens.AccessToken, seeded);

        (await ErrorsAsync(response)).Values.SelectMany(messages => messages).ShouldBe([ClosedMessage]);
    }

    [Test]
    public async Task Custodial_SeedTransactionOfADisabledCustomer_ReturnsTheDisabledRefusal()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        var seeded = await InsertSeedTransferInAsync(tenant.Id, accountId, 100m);
        await DisableCustomerAsync(customer.Id);

        var response = await ReverseAsync(tokens.AccessToken, seeded);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
    }

    [Test]
    public async Task Custodial_SeedTradeOnAnAssignedAccount_ReturnsTheSourceRefusal_NotTheTradeLock()
    {
        var world = await RebalanceTrades.AssignedAsync();
        var seeded = await InsertSeedBuyAsync(world.AccountId, world.First);

        var response = await ReverseAsync(world.AccessToken, seeded);

        var messages = (await ErrorsAsync(response)).Values.SelectMany(values => values).ToList();
        messages.ShouldBe([Message]);
        messages.ShouldNotContain(TradeLockMessage);
    }

    [Test]
    public async Task AnotherTenantsSeedTransaction_Returns404()
    {
        var (otherTenant, _, otherCustomer, otherTokens) = await AccountHttp.SignInAdviser(
            "South Advisory", "south-advisory", "sam@south.example", customerEmail: "sue@south.example");
        var otherAccount = await OpenCustodialAsync(otherTokens.AccessToken, otherCustomer.PublicId, "Theirs", "NZD");
        var seeded = await InsertSeedTransferInAsync(otherTenant.Id, otherAccount, 100m);
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();

        var response = await ReverseAsync(tokens.AccessToken, seeded);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> ReverseWithKeyAsync(string accessToken, Guid transactionId, Guid key) =>
        TransactionHttp.Send(HttpMethod.Post, $"/transactions/{transactionId}/reverse", accessToken, idempotencyKey: key);

    /// <summary>A <c>Seed</c> TransferIn built with the factory and saved directly, as the seeds write it (R6).</summary>
    private static async Task<Guid> InsertSeedTransferInAsync(int tenantId, Guid accountPublicId, decimal amount)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        var transaction = Transaction.Post(
            tenantId,
            account.Id,
            TransactionType.TransferIn,
            amount,
            account.Currency,
            DateTimeOffset.UtcNow.AddDays(-1),
            memo: null,
            reference: null,
            TransactionSource.Seed,
            currentCashSum: 0m,
            existingTransactionCount: 0);
        return await SaveAsync(db, transaction);
    }

    /// <summary>A <c>Seed</c> Buy built with the factory and saved directly; the holding is not projected.</summary>
    private static async Task<Guid> InsertSeedBuyAsync(Guid accountPublicId, Guid instrumentPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentPublicId);
        var transaction = Transaction.PostBuy(
            account.TenantId,
            account.Id,
            account.Type,
            account.Currency,
            instrument.Id,
            1m,
            Money.Create(10m, instrument.QuoteCurrency.Trim()),
            fxRate: 1m,
            DateTimeOffset.UtcNow.AddDays(-1),
            memo: null,
            reference: null,
            TransactionSource.Seed,
            currentCashSum: 1000m);
        return await SaveAsync(db, transaction);
    }

    private static async Task<Guid> SaveAsync(ApplicationDbContext db, Transaction transaction)
    {
        transaction.CreatedBy = "test";
        transaction.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        transaction.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.PublicId;
    }

    private static async Task DisableCustomerAsync(int customerId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.Id == customerId);
        person.Disable();
        await db.SaveChangesAsync();
    }
}
