using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Withdrawals R47 (roadmap D14): cash moves between Custodial accounts of one Customer only.</summary>
[Parallelizable]
public class CustodialTransferSameCustomerTests : TestBase
{
    private const string TransferPath = "/transactions/transfer";
    private const string Message = "Cash can be transferred between Custodial accounts of the same customer only.";

    [Test]
    public async Task BetweenTwoCustomersCustodialAccounts_Returns400AtToAccountId_AndWritesNothing()
    {
        var (tenant, adviser, ned, tokens) = await AccountHttp.SignInTenantAdmin();
        var olga = await TestApp.CreatePersonAsync(UserRole.Customer, "Olga Other", "olga@north.example", "Password1!", tenant.Id, adviser.Id);
        var from = await OpenCustodialAsync(tokens.AccessToken, ned.PublicId, "Ned's");
        var to = await OpenCustodialAsync(tokens.AccessToken, olga.PublicId, "Olga's");
        (await FundAsync(tokens.AccessToken, from, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await PostAsync(tokens.AccessToken, Transfer(from, to, 40m), TransferPath);

        (await ErrorsAsync(response))["ToAccountId"].ShouldBe([Message]);
        (await CashBalanceAsync(tokens.AccessToken, from)).ShouldBe(100m);
        (await TransactionCountAsync(to)).ShouldBe(0);
    }

    [Test]
    public async Task BetweenOneCustomersCustodialAccounts_Returns201()
    {
        var (_, _, ned, tokens) = await AccountHttp.SignInTenantAdmin();
        var from = await OpenCustodialAsync(tokens.AccessToken, ned.PublicId, "First");
        var to = await OpenCustodialAsync(tokens.AccessToken, ned.PublicId, "Second");
        (await FundAsync(tokens.AccessToken, from, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(tokens.AccessToken, Transfer(from, to, 40m), TransferPath)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task BetweenTwoCustomersExternalAccounts_Returns201()
    {
        var (tenant, adviser, ned, tokens) = await AccountHttp.SignInTenantAdmin();
        var olga = await TestApp.CreatePersonAsync(UserRole.Customer, "Olga Other", "olga@north.example", "Password1!", tenant.Id, adviser.Id);
        var from = await OpenExternalAsync(tokens.AccessToken, ned.PublicId);
        var to = await OpenExternalAsync(tokens.AccessToken, olga.PublicId);
        (await FundAsync(tokens.AccessToken, from, 100m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(tokens.AccessToken, Transfer(from, to, 40m).WithBookedAt(), TransferPath)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AnExistingTransferBetweenTwoCustomers_CanStillBeReversed()
    {
        var (tenant, adviser, ned, tokens) = await AccountHttp.SignInTenantAdmin();
        var olga = await TestApp.CreatePersonAsync(UserRole.Customer, "Olga Other", "olga@north.example", "Password1!", tenant.Id, adviser.Id);
        var from = await OpenCustodialAsync(tokens.AccessToken, ned.PublicId, "Ned's");
        var to = await OpenCustodialAsync(tokens.AccessToken, olga.PublicId, "Olga's");
        (await FundAsync(tokens.AccessToken, from, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var transfer = await InsertTransferAsync(from, to, 40m);

        var response = await ReverseAsync(tokens.AccessToken, transfer);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await CashBalanceAsync(tokens.AccessToken, from)).ShouldBe(100m);
    }

    private static Dictionary<string, object?> Transfer(Guid from, Guid to, decimal amount) => new()
    {
        ["fromAccountId"] = from,
        ["toAccountId"] = to,
        ["amount"] = amount
    };

    /// <summary>A transfer written straight to the database, as it could be entered before this slice.</summary>
    private static async Task<Guid> InsertTransferAsync(Guid fromId, Guid toId, decimal amount)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var from = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == fromId);
        var to = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == toId);
        var sum = await db.TransactionCashLegs.IgnoreQueryFilters().Where(leg => leg.AccountId == from.Id).SumAsync(leg => leg.Amount);
        var transaction = Domain.Entities.Transaction.PostInternalTransfer(
            from.TenantId, from.Id, from.Currency, from.CustodyMode, to.Id, to.Currency, to.CustodyMode,
            Money.Create(amount, from.Currency), DateTimeOffset.UtcNow, null, null, TransactionSource.Manual, sum);
        transaction.CreatedBy = "test";
        transaction.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.PublicId;
    }
}
