using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Withdrawals;

/// <summary>Writes withdrawals straight to the database, in any status, for tests of what reads them.</summary>
internal static class WithdrawalDb
{
    /// <summary>A withdrawal of <paramref name="amount"/> from the Account in <paramref name="status"/>, to a Verified
    /// nominated bank account of the Account's Customer added for the purpose.</summary>
    public static async Task<Withdrawal> InsertAsync(Guid accountPublicId, decimal amount, WithdrawalStatus status)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == accountPublicId);
        var customer = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == account.CustomerId);
        var adder = customer.AdviserId ?? customer.Id;
        var at = DateTimeOffset.UtcNow;
        var currency = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == account.Currency);

        var nominated = NominatedBankAccount.Add(
            account.TenantId, customer.Id, currency, "ANZ", "J Smith", "N-" + Guid.NewGuid().ToString("N")[..20], adder, at);
        nominated.Verify(customer.Id, "Seeded", at);
        nominated.CreatedBy = "test";
        db.NominatedBankAccounts.Add(nominated);
        await db.SaveChangesAsync();

        var withdrawal = Withdrawal.Request(
            account.TenantId, account.Id, nominated.Id, amount, account.Currency, "TEST", null, adder, at);
        var approver = customer.Id;
        switch (status)
        {
            case WithdrawalStatus.Requested:
                break;
            case WithdrawalStatus.Rejected:
                withdrawal.Reject(approver, "No", at);
                break;
            case WithdrawalStatus.Cancelled:
                withdrawal.Cancel(adder, null, at);
                break;
            default:
                withdrawal.Approve(approver, null, at);
                if (status == WithdrawalStatus.Sent)
                {
                    withdrawal.MarkSent();
                }
                else if (status == WithdrawalStatus.Failed)
                {
                    withdrawal.MarkFailed(PaymentRejectReason.Refused);
                }
                else if (status is WithdrawalStatus.Paid or WithdrawalStatus.Returned)
                {
                    var anyTransaction = await db.TransactionCashLegs.IgnoreQueryFilters().AsNoTracking()
                        .Where(leg => leg.AccountId == account.Id).Select(leg => leg.TransactionId).FirstAsync();
                    withdrawal.MarkPaid(anyTransaction);
                    if (status == WithdrawalStatus.Returned)
                    {
                        withdrawal.MarkReturned(anyTransaction, "Seeded");
                    }
                }

                break;
        }

        withdrawal.CreatedBy = "test";
        db.Withdrawals.Add(withdrawal);
        await db.SaveChangesAsync();
        return withdrawal;
    }
}
