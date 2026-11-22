using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Withdrawals R45 and account closure R23: no hand <c>TransferOut</c> and no hand <c>CloseOut</c> on a
/// Custodial account; the account closure writes off a remainder.</summary>
[Parallelizable]
public class CustodialTransferOutRefusedTests : TestBase
{
    private const string TransferOutMessage =
        "Withdrawals from a Custodial account are paid to the customer's nominated bank account. Request a withdrawal.";

    private const string CloseOutMessage =
        "The remainder of a Custodial account is written off by its account closure. Request an account closure.";

    [Test]
    public async Task TransferOut_ByTenantAdminAndAdviser_Gets400OnType_AndNothingIsWritten()
    {
        var (tenant, _, customer, tenantAdmin) = await AccountHttp.SignInTenantAdmin();
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", tenant.Code);
        var accountId = await OpenCustodialAsync(tenantAdmin.AccessToken, customer.PublicId);
        (await FundAsync(tenantAdmin.AccessToken, accountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        foreach (var token in new[] { tenantAdmin.AccessToken, adviser.AccessToken })
        {
            (await ErrorsAsync(await PostAsync(token, Cash(accountId, "TransferOut", -40m))))["Type"].ShouldBe([TransferOutMessage]);
        }

        (await CashBalanceAsync(tenantAdmin.AccessToken, accountId)).ShouldBe(100m);
    }

    [Test]
    public async Task TransferOut_BySystemAdmin_Gets400OnType()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        (await FundAsync(tokens.AccessToken, accountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var system = await TenantHttp.SignInSystemAdmin();
        var body = Cash(accountId, "TransferOut", -40m);
        body["tenantId"] = tenant.PublicId;

        (await ErrorsAsync(await PostAsync(system.AccessToken, body)))["Type"].ShouldBe([TransferOutMessage]);
    }

    [Test]
    public async Task ExternalAccount_StillTakesAHandTransferOutAndAWholeCloseOut()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId);
        (await FundAsync(tokens.AccessToken, accountId, 100m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(tokens.AccessToken, Cash(accountId, "TransferOut", -40m).WithBookedAt())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, Cash(accountId, "CloseOut", -60m).WithBookedAt())).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AManualTransferOutEnteredBeforeThisSlice_CanStillBeReversedByHand()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        (await FundAsync(tokens.AccessToken, accountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var transferOut = await InsertManualTransferOutAsync(accountId, 40m);

        var response = await ReverseAsync(tokens.AccessToken, transferOut);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(100m);
    }

    [Test]
    public async Task CloseOut_OfARemainderBelowTheMinorUnit_Gets400OnType_ForEveryCaller()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrument = await CreateInstrumentAsync(tokens.AccessToken, price: 3.00m);
        (await FundAsync(tokens.AccessToken, accountId, 1.00m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await InsertManualTradeAsync(accountId, "Buy", instrument, 0.332m, price: 3.00m);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0.004m);
        var system = await TenantHttp.SignInSystemAdmin();
        var systemBody = Cash(accountId, "CloseOut", -0.004m);
        systemBody["tenantId"] = tenant.PublicId;

        (await ErrorsAsync(await PostAsync(tokens.AccessToken, Cash(accountId, "CloseOut", -0.004m))))["Type"].ShouldBe([CloseOutMessage]);
        (await ErrorsAsync(await PostAsync(system.AccessToken, systemBody)))["Type"].ShouldBe([CloseOutMessage]);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0.004m);
    }

    [Test]
    public async Task AManualCloseOutEnteredBeforeThisSlice_CanStillBeReversedByHand()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        await InsertManualTransferInAsync(accountId, 0.004m);
        var closeOut = await InsertManualCloseOutAsync(accountId, 0.004m);

        var response = await ReverseAsync(tokens.AccessToken, closeOut);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0.004m);
    }

    [Test]
    public async Task CloseOut_OfAWholeBalance_Gets400OnType()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        (await FundAsync(tokens.AccessToken, accountId, 5.00m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await ErrorsAsync(await PostAsync(tokens.AccessToken, Cash(accountId, "CloseOut", -5.00m))))["Type"].ShouldBe([CloseOutMessage]);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(5.00m);
    }

    [Test]
    public async Task CloseOut_OfAnEmptyAccount_Gets400OnType()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);

        (await ErrorsAsync(await PostAsync(tokens.AccessToken, Cash(accountId, "CloseOut", 0m))))["Type"].ShouldBe([CloseOutMessage]);
    }

    /// <summary>A hand <c>CloseOut</c> of the remainder, source <c>Manual</c>, written straight to the database as
    /// withdrawals R46 allowed before account closure.</summary>
    private static async Task<Guid> InsertManualCloseOutAsync(Guid accountId, decimal remainder)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var transaction = Domain.Entities.Transaction.Post(
            account.TenantId, account.Id, TransactionType.CloseOut, -remainder, account.Currency, DateTimeOffset.UtcNow,
            memo: null, reference: null, TransactionSource.Manual, currentCashSum: remainder, existingTransactionCount: 1);
        transaction.CreatedBy = "test";
        transaction.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.PublicId;
    }

    private static async Task<Guid> InsertManualTransferOutAsync(Guid accountId, decimal amount)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var sum = await db.TransactionCashLegs.IgnoreQueryFilters().Where(leg => leg.AccountId == account.Id).SumAsync(leg => leg.Amount);
        var transaction = Domain.Entities.Transaction.Post(
            account.TenantId, account.Id, TransactionType.TransferOut, -amount, account.Currency, DateTimeOffset.UtcNow,
            memo: null, reference: null, TransactionSource.Manual, currentCashSum: sum, existingTransactionCount: 1);
        transaction.CreatedBy = "test";
        transaction.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.PublicId;
    }
}
