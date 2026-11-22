using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Withdrawals;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.AccountClosures;

/// <summary>Scope and item of account closures (account-closure R1, R25–R27).</summary>
internal static class AccountClosureReader
{
    /// <summary>R1: the internal id of the Account of the path, in the caller's scope, or 404.</summary>
    public static async Task<int> AccountIdAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid accountPublicId,
        CancellationToken cancellationToken) =>
        await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
            .Where(row => row.PublicId == accountPublicId)
            .Select(row => (int?)row.Id)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Account", accountPublicId);

    /// <summary>R27: the latest closure of each Account, by requested time.</summary>
    public static async Task<Dictionary<int, AccountClosureSummaryDto>> LatestAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        var rows = await db.AccountClosures.AsNoTracking()
            .Where(row => accountIds.Contains(row.AccountId))
            .Select(row => new
            {
                row.Id,
                row.AccountId,
                row.PublicId,
                row.Status,
                row.PayoutAmount,
                row.WithdrawalId,
                row.RequestedAt,
                row.CompletedAt
            })
            .ToListAsync(cancellationToken);
        var latest = rows
            .GroupBy(row => row.AccountId)
            .Select(group => group.OrderByDescending(row => row.RequestedAt).ThenByDescending(row => row.Id).First())
            .ToList();
        var withdrawalIds = latest.Where(row => row.WithdrawalId is not null).Select(row => row.WithdrawalId!.Value).ToList();
        var withdrawals = await db.Withdrawals.AsNoTracking()
            .Where(row => withdrawalIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        return latest.ToDictionary(row => row.AccountId, row => new AccountClosureSummaryDto
        {
            Id = row.PublicId,
            Status = row.Status.ToString(),
            PayoutAmount = row.PayoutAmount,
            WithdrawalId = row.WithdrawalId is int withdrawal ? withdrawals[withdrawal] : null,
            RequestedAt = row.RequestedAt,
            CompletedAt = row.CompletedAt
        });
    }

    public static async Task<List<AccountClosureDto>> ToDtosAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IReadOnlyList<AccountClosure> rows,
        CancellationToken cancellationToken)
    {
        var accountIds = rows.Select(row => row.AccountId).Distinct().ToList();
        var nominatedIds = rows.Where(row => row.NominatedBankAccountId is not null)
            .Select(row => row.NominatedBankAccountId!.Value).Distinct().ToList();
        var userIds = rows.Select(row => row.RequestedByUserId).Distinct().ToList();
        var withdrawalIds = rows.Where(row => row.WithdrawalId is not null).Select(row => row.WithdrawalId!.Value).ToList();
        var transactionIds = rows.Where(row => row.WriteOffTransactionId is not null)
            .Select(row => row.WriteOffTransactionId!.Value).ToList();

        var accounts = await (
                from account in db.Accounts.AsNoTracking()
                join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
                where accountIds.Contains(account.Id)
                select new { account.Id, account.PublicId, CustomerPublicId = customer.PublicId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var payees = await db.NominatedBankAccounts.AsNoTracking()
            .Where(row => nominatedIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new WithdrawalPayeeDto
            {
                Id = row.PublicId,
                BankName = row.BankName,
                AccountName = row.AccountName,
                AccountNumber = row.AccountNumber
            }, cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);
        var withdrawals = await db.Withdrawals.AsNoTracking()
            .Where(row => withdrawalIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var transactions = await db.Transactions.AsNoTracking()
            .Where(row => transactionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);

        // R25: a Customer sees every field except the memo.
        var customerView = currentUser.Role == UserRole.Customer;
        return rows.Select(row => new AccountClosureDto
        {
            Id = row.PublicId,
            AccountId = accounts[row.AccountId].PublicId,
            CustomerId = accounts[row.AccountId].CustomerPublicId,
            Status = row.Status.ToString(),
            CancelReason = row.CancelReason?.ToString(),
            Memo = customerView ? null : row.Memo,
            RequestedBy = users[row.RequestedByUserId],
            RequestedAt = row.RequestedAt,
            Currency = row.Currency.Trim(),
            CashBalanceAtRequest = row.CashBalanceAtRequest,
            PayoutAmount = row.PayoutAmount,
            NominatedBankAccount = row.NominatedBankAccountId is int nominated
                ? new WithdrawalPayeeDto
                {
                    Id = payees[nominated].Id,
                    BankName = payees[nominated].BankName,
                    AccountName = payees[nominated].AccountName,
                    AccountNumber = AccountNumberMask.Mask(payees[nominated].AccountNumber)!
                }
                : null,
            WithdrawalId = row.WithdrawalId is int withdrawal ? withdrawals[withdrawal] : null,
            WriteOffAmount = row.WriteOffAmount,
            WriteOffTransactionId = row.WriteOffTransactionId is int transaction ? transactions[transaction] : null,
            CompletedAt = row.CompletedAt,
            EndedAt = row.EndedAt
        }).ToList();
    }
}
