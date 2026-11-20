using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Withdrawals;

/// <summary>Scope and item of withdrawals (withdrawals R17, R25).</summary>
internal static class WithdrawalReader
{
    /// <summary>R17: the withdrawals of the Accounts in the caller's scope.</summary>
    public static IQueryable<Withdrawal> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var accounts = AccountScope.ManagedAccounts(db, currentUser);
        return db.Withdrawals.Where(row => accounts.Any(account => account.Id == row.AccountId));
    }

    public static async Task<List<WithdrawalDto>> ToDtosAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IReadOnlyList<Withdrawal> rows,
        CancellationToken cancellationToken)
    {
        var accountIds = rows.Select(row => row.AccountId).Distinct().ToList();
        var nominatedIds = rows.Select(row => row.NominatedBankAccountId).Distinct().ToList();
        var userIds = rows.Select(row => row.RequestedByUserId)
            .Concat(rows.Where(row => row.DecisionByUserId is not null).Select(row => row.DecisionByUserId!.Value))
            .Distinct()
            .ToList();
        var transactionIds = rows.Where(row => row.PayoutTransactionId is not null).Select(row => row.PayoutTransactionId!.Value)
            .Concat(rows.Where(row => row.ReturnTransactionId is not null).Select(row => row.ReturnTransactionId!.Value))
            .Distinct()
            .ToList();

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
        var transactions = await db.Transactions.AsNoTracking()
            .Where(row => transactionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);

        // R25: a Customer reads neither the memo nor the decision's note.
        var customerView = currentUser.Role == UserRole.Customer;
        return rows.Select(row =>
        {
            var payee = payees[row.NominatedBankAccountId];
            return new WithdrawalDto
            {
                Id = row.PublicId,
                AccountId = accounts[row.AccountId].PublicId,
                CustomerId = accounts[row.AccountId].CustomerPublicId,
                NominatedBankAccount = new WithdrawalPayeeDto
                {
                    Id = payee.Id,
                    BankName = payee.BankName,
                    AccountName = payee.AccountName,
                    AccountNumber = AccountNumberMask.Mask(payee.AccountNumber)!
                },
                Amount = row.Amount,
                Currency = row.Currency.Trim(),
                Reference = row.Reference,
                Memo = customerView ? null : row.Memo,
                Status = row.Status.ToString(),
                RequestedBy = users[row.RequestedByUserId],
                RequestedAt = row.RequestedAt,
                Decision = row.DecisionAction is { } action && row.DecisionByUserId is int by
                    ? new WithdrawalDecisionDto
                    {
                        Action = action.ToString(),
                        By = users[by],
                        At = row.DecisionAt!.Value,
                        Note = customerView ? null : row.DecisionNote
                    }
                    : null,
                PayoutTransactionId = row.PayoutTransactionId is int payout ? transactions[payout] : null,
                ReturnTransactionId = row.ReturnTransactionId is int returned ? transactions[returned] : null,
                FailureReason = row.FailureReason?.ToString(),
                ReturnReason = row.ReturnReason,
                RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
            };
        }).ToList();
    }
}
