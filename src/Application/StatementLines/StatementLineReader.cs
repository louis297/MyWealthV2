using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

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
        var relatedIds = lines.Where(line => line.RelatedLineId is not null).Select(line => line.RelatedLineId!.Value).Distinct().ToList();
        var withdrawalIds = lines.Where(line => line.WithdrawalId is not null).Select(line => line.WithdrawalId!.Value).Distinct().ToList();

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
        var related = await db.BankStatementLines.AsNoTracking()
            .Where(row => relatedIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var withdrawals = await db.Withdrawals.AsNoTracking()
            .Where(row => withdrawalIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var lineIds = lines.Select(line => line.Id).ToList();
        var returnPayments = (await db.OutgoingPayments.AsNoTracking()
                .Where(row => row.Kind == OutgoingPaymentKind.ReceiptReturn && lineIds.Contains(row.ReturnOfLineId!.Value))
                .Select(row => new { LineId = row.ReturnOfLineId!.Value, row.Id, row.Status, row.Attempts, row.LastFailure })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.LineId)
            .ToDictionary(group => group.Key, group => group.MaxBy(row => row.Id)!);
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new StatementLinePersonDto(row.PublicId, row.Name), cancellationToken);

        return lines.Select(line => new StatementLineDto
        {
            Id = line.PublicId,
            ClientMoneyAccountId = clientMoney[line.ClientMoneyAccountId],
            Currency = line.Currency.Trim(),
            Direction = line.Direction.ToString(),
            Kind = line.Kind.ToString(),
            Amount = line.Amount,
            ValueDate = line.ValueDate.ToString("yyyy-MM-dd"),
            PayerName = line.PayerName,
            PayerAccount = line.PayerAccountMasked,
            Reference = line.ReferenceText,
            ReceivedAt = line.ReceivedAt,
            Status = line.Status.ToString(),
            UnmatchedReason = line.UnmatchedReason?.ToString(),
            // Bank-returns R10, R11: a match with no Transaction has null account, customer and transaction.
            Match = line.MatchMethod is { } method
                ? new StatementLineMatchDto
                {
                    AccountId = line.MatchedAccountId is int accountId ? accounts[accountId].PublicId : null,
                    CustomerId = line.MatchedAccountId is int customerOf ? accounts[customerOf].CustomerPublicId : null,
                    TransactionId = line.MatchedTransactionId is int transactionId ? transactions[transactionId] : null,
                    Method = method.ToString(),
                    At = line.MatchedAt!.Value
                }
                : null,
            LastAction = line.LastAction is { } action && line.LastActionAt is not null
                ? new StatementLineLastActionDto
                {
                    Action = action.ToString(),
                    Note = line.LastActionNote,
                    By = line.LastActionByUserId is int byUserId ? users[byUserId] : null,
                    At = line.LastActionAt!.Value
                }
                : null,
            RelatedLineId = line.RelatedLineId is int relatedId ? related[relatedId] : null,
            WithdrawalId = line.WithdrawalId is int withdrawalId ? withdrawals[withdrawalId] : null,
            ReturnReason = line.ReturnReason,
            ReturnPayment = returnPayments.TryGetValue(line.Id, out var payment)
                ? new StatementLineReturnPaymentDto
                {
                    Status = payment.Status.ToString(),
                    Attempts = payment.Attempts,
                    LastFailure = payment.LastFailure?.ToString()
                }
                : null,
            RowVersion = Convert.ToBase64String(line.RowVersion ?? [])
        }).ToList();
    }
}
