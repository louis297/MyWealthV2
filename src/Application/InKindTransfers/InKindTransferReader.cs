using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.InKindTransfers;

/// <summary>Scope and item of in-kind transfers (in-kind-transfers R1, R28).</summary>
internal static class InKindTransferReader
{
    /// <summary>R1: the transfers of the Accounts in the caller's scope, as withdrawals R17.</summary>
    public static IQueryable<InKindTransfer> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var accounts = AccountScope.ManagedAccounts(db, currentUser);
        return db.InKindTransfers.Where(row => accounts.Any(account => account.Id == row.AccountId));
    }

    public static async Task<List<InKindTransferDto>> ToDtosAsync(
        IApplicationDbContext db, ICurrentUser currentUser, IReadOnlyList<InKindTransfer> rows, CancellationToken cancellationToken)
    {
        var accountIds = rows.Select(row => row.AccountId).Distinct().ToList();
        var instrumentIds = rows.Select(row => row.InstrumentId).Distinct().ToList();
        var userIds = rows.Select(row => row.RequestedByUserId)
            .Concat(rows.Where(row => row.DecisionByUserId is not null).Select(row => row.DecisionByUserId!.Value))
            .Concat(rows.Where(row => row.SetAsideByUserId is not null).Select(row => row.SetAsideByUserId!.Value))
            .Distinct()
            .ToList();
        var transactionIds = rows.Where(row => row.TransactionId is not null).Select(row => row.TransactionId!.Value).ToList();

        var accounts = await (
                from account in db.Accounts.AsNoTracking()
                join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
                where accountIds.Contains(account.Id)
                select new { account.Id, account.PublicId, CustomerPublicId = customer.PublicId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var instruments = await db.Instruments.AsNoTracking()
            .Where(row => instrumentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new InKindTransferInstrumentDto(row.PublicId, row.Symbol, row.Name), cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);
        var transactions = await db.Transactions.AsNoTracking()
            .Where(row => transactionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);

        // R28: a Customer reads neither the memo, the notes nor the send.
        var customerView = currentUser.Role == UserRole.Customer;
        return rows.Select(row => new InKindTransferDto
        {
            Id = row.PublicId,
            AccountId = accounts[row.AccountId].PublicId,
            CustomerId = accounts[row.AccountId].CustomerPublicId,
            Direction = row.Direction.ToString(),
            Instrument = instruments[row.InstrumentId],
            Quantity = row.Quantity,
            Currency = row.Currency.Trim(),
            Cost = row.Cost,
            AcquiredOn = row.AcquiredOn?.ToString("yyyy-MM-dd"),
            ChangesBeneficialOwner = row.ChangesBeneficialOwner,
            Counterparty = new InKindTransferCounterpartyDto(row.CounterpartyName, row.CounterpartyAccount, row.CounterpartyHolderName),
            Memo = customerView ? null : row.Memo,
            Status = row.Status.ToString(),
            RequestedBy = users[row.RequestedByUserId],
            RequestedAt = row.RequestedAt,
            Decision = row.DecisionAction is { } action && row.DecisionByUserId is int by
                ? new InKindTransferDecisionDto
                {
                    Action = action.ToString(),
                    By = users[by],
                    At = row.DecisionAt!.Value,
                    Note = customerView ? null : row.DecisionNote
                }
                : null,
            Send = customerView
                ? null
                : new InKindTransferSendDto(row.Attempts, row.LastAttemptAt, row.LastFailure?.ToString(), row.AcceptedAt,
                    row.CustodianTransferId),
            FailureReason = row.FailureReason?.ToString(),
            SetAside = row.SetAsideByUserId is int setAsideBy
                ? new InKindTransferSetAsideDto
                {
                    By = users[setAsideBy],
                    At = row.SetAsideAt!.Value,
                    Note = customerView ? null : row.SetAsideNote
                }
                : null,
            TransactionId = row.TransactionId is int transaction ? transactions[transaction] : null,
            CompletedAt = row.CompletedAt,
            RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
        }).ToList();
    }
}
