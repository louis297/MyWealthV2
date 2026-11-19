using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>fixes/ledger-write-guards.md B2: a caller's cash amount fits the Account currency's decimal places.</summary>
[Parallelizable]
public class CashAmountDecimalPlacesTests : TestBase
{
    private const string BookedAt = "2026-11-10T09:00:00Z";

    [TestCase("TransferIn", 10.123)]
    [TestCase("TransferOut", -10.123)]
    [TestCase("Interest", 10.123)]
    [TestCase("Interest", -10.123)]
    [TestCase("Opening", 10.123)]
    public async Task CashType_MoreDecimalsThanUsd_Returns400AndWritesNothing(string type, double amount)
    {
        var world = await LegAccountsFixture.CreateAsync();
        var accountId = await InternalTransferTests.OpenAccountAsync(
            world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned USD", "USD");
        if (type is "TransferOut" or "Interest")
        {
            await InternalTransferTests.FundAsync(world, accountId, 100m);
        }

        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);
        var key = Guid.NewGuid();

        var response = await PostCashAsync(world.TenantAdmin.AccessToken, accountId, type, (decimal)amount, key);

        await AssertAmountErrorAsync(response);
        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
        (await IdempotencyRecordExistsAsync(world.Tenant.Id, key)).ShouldBeFalse();
    }

    [Test]
    public async Task Jpy_AFractionIsRefused_AWholeAmountIsAccepted()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var accountId = await InternalTransferTests.OpenAccountAsync(
            world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned JPY", "JPY");

        await AssertAmountErrorAsync(
            await PostCashAsync(world.TenantAdmin.AccessToken, accountId, "TransferIn", 100.5m));
        (await PostCashAsync(world.TenantAdmin.AccessToken, accountId, "TransferIn", 100m))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Usd_TrailingZerosFit()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var accountId = await InternalTransferTests.OpenAccountAsync(
            world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned USD", "USD");

        (await PostCashAsync(world.TenantAdmin.AccessToken, accountId, "TransferIn", 10.10m))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(world.TenantAdmin.AccessToken, accountId, "TransferIn", 10.100m))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InternalTransferTests.CashBalanceAsync(world.TenantAdmin.AccessToken, accountId)).ShouldBe(20.2m);
    }

    [Test]
    public async Task InternalTransfer_MoreDecimalsThanUsd_Returns400AndWritesNothing()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var from = await InternalTransferTests.OpenAccountAsync(
            world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned USD", "USD");
        var to = await InternalTransferTests.OpenAccountAsync(
            world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned USD savings", "USD");
        await InternalTransferTests.FundAsync(world, from, 100m);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);
        var key = Guid.NewGuid();

        var response = await InternalTransferTests.PostAsync(
            world.TenantAdmin.AccessToken, InternalTransferTests.Body(from, to, 10.123m), key);

        await AssertAmountErrorAsync(response);
        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
        (await IdempotencyRecordExistsAsync(world.Tenant.Id, key)).ShouldBeFalse();
    }

    [Test]
    public async Task CloseOut_IsExempt_ABalanceBelowTheMinorUnitCanBeClosed()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var accountId = await InternalTransferTests.OpenAccountAsync(
            world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned USD", "USD");
        await InsertTransferInAsync(world.Tenant.Id, accountId, 0.0034m);
        (await InternalTransferTests.CashBalanceAsync(world.TenantAdmin.AccessToken, accountId)).ShouldBe(0.0034m);

        var closeOut = await PostCashAsync(world.TenantAdmin.AccessToken, accountId, "CloseOut", -0.0034m);
        closeOut.StatusCode.ShouldBe(HttpStatusCode.Created, await closeOut.Content.ReadAsStringAsync());

        (await InternalTransferTests.CashBalanceAsync(world.TenantAdmin.AccessToken, accountId)).ShouldBe(0m);
        (await CloseAsync(world.TenantAdmin.AccessToken, accountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static Task<HttpResponseMessage> PostCashAsync(
        string accessToken,
        Guid accountId,
        string type,
        decimal amount,
        Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type,
            amount,
            bookedAt = BookedAt
        }, key ?? Guid.NewGuid());

    private static async Task AssertAmountErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => property.Name)
            .ShouldContain(name => string.Equals(name, "amount", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A cash leg with more decimals than the currency, as a Buy or Sell can leave (buy-sell R12).</summary>
    private static async Task InsertTransferInAsync(int tenantId, Guid accountPublicId, decimal amount)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
        var transaction = Transaction.Post(
            tenantId,
            account.Id,
            TransactionType.TransferIn,
            amount,
            account.Currency,
            DateTimeOffset.Parse(BookedAt),
            memo: null,
            reference: null,
            source: TransactionSource.Manual,
            currentCashSum: 0m,
            existingTransactionCount: 0);
        transaction.CreatedBy = "test";
        foreach (var leg in transaction.CashLegs)
        {
            leg.CreatedBy = "test";
        }

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
    }

    private static async Task<bool> IdempotencyRecordExistsAsync(int tenantId, Guid key)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.IdempotencyRecords.AnyAsync(record => record.TenantId == tenantId && record.Key == key);
    }

    private static async Task<HttpResponseMessage> CloseAsync(string accessToken, Guid accountId)
    {
        var get = await Accounts.AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        return await Accounts.AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
    }
}
