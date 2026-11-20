using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.StatementLines;

/// <summary>Reads statement lines of the current tenant and maps them to the item (deposit-matching R21, §8.1).</summary>
internal static class StatementLineReader
{
    public static int CurrentTenantId(ICurrentUser currentUser) =>
        currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");

    /// <summary>The line of the current tenant, tracked; another tenant's line or a missing one is 404.</summary>
    public static async Task<BankStatementLine> FindAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId(currentUser);
        return await db.BankStatementLines
                   .SingleOrDefaultAsync(row => row.PublicId == id && row.TenantId == tenantId, cancellationToken)
               ?? throw new NotFoundException("Statement line", id);
    }

    public static async Task<List<StatementLineDto>> ToDtosAsync(
        IApplicationDbContext db,
        IReadOnlyList<BankStatementLine> lines,
        CancellationToken cancellationToken)
    {
        var clientMoneyIds = lines.Select(line => line.ClientMoneyAccountId).Distinct().ToList();
        var accountIds = lines.Where(line => line.MatchedAccountId is not null).Select(line => line.MatchedAccountId!.Value).Distinct().ToList();
        var transactionIds = lines.Where(line => line.MatchedTransactionId is not null).Select(line => line.MatchedTransactionId!.Value).Distinct().ToList();
        var userIds = lines.Where(line => line.LastActionByUserId is not null).Select(line => line.LastActionByUserId!.Value).Distinct().ToList();

        var clientMoney = await db.ClientMoneyAccounts.AsNoTracking()
            .Where(row => clientMoneyIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var accounts = await (
                from account in db.Accounts.AsNoTracking()
                join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
                where accountIds.Contains(account.Id)
                select new { account.Id, account.PublicId, CustomerPublicId = customer.PublicId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var transactions = await db.Transactions.AsNoTracking()
            .Where(row => transactionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new StatementLinePersonDto(row.PublicId, row.Name), cancellationToken);

        return lines.Select(line => new StatementLineDto
        {
            Id = line.PublicId,
            ClientMoneyAccountId = clientMoney[line.ClientMoneyAccountId],
            Currency = line.Currency.Trim(),
            Direction = line.Direction.ToString(),
            Amount = line.Amount,
            ValueDate = line.ValueDate.ToString("yyyy-MM-dd"),
            PayerName = line.PayerName,
            PayerAccount = line.PayerAccountMasked,
            Reference = line.ReferenceText,
            ReceivedAt = line.ReceivedAt,
            Status = line.Status.ToString(),
            UnmatchedReason = line.UnmatchedReason?.ToString(),
            Match = line.MatchedAccountId is int accountId && line.MatchedTransactionId is int transactionId
                ? new StatementLineMatchDto
                {
                    AccountId = accounts[accountId].PublicId,
                    CustomerId = accounts[accountId].CustomerPublicId,
                    TransactionId = transactions[transactionId],
                    Method = line.MatchMethod!.Value.ToString(),
                    At = line.MatchedAt!.Value
                }
                : null,
            LastAction = line.LastAction is { } action && line.LastActionByUserId is int byUserId
                ? new StatementLineLastActionDto
                {
                    Action = action.ToString(),
                    Note = line.LastActionNote,
                    By = users[byUserId],
                    At = line.LastActionAt!.Value
                }
                : null,
            RowVersion = Convert.ToBase64String(line.RowVersion ?? [])
        }).ToList();
    }
}
