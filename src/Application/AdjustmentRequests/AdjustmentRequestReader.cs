using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Scope and item of adjustment requests (adjustment-review R1, R19).</summary>
internal static class AdjustmentRequestReader
{
    /// <summary>R1: the requests of the caller's Tenant.</summary>
    public static IQueryable<AdjustmentRequest> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var tenantId = currentUser.TenantId;
        return db.AdjustmentRequests.Where(row => row.TenantId == tenantId);
    }

    public static async Task<List<AdjustmentRequestDto>> ToDtosAsync(
        IApplicationDbContext db, IReadOnlyList<AdjustmentRequest> rows, CancellationToken cancellationToken)
    {
        var accountIds = rows.Select(row => row.AccountId).Distinct().ToList();
        var instrumentIds = rows.Where(row => row.InstrumentId is not null).Select(row => row.InstrumentId!.Value).Distinct().ToList();
        var userIds = rows.Select(row => row.RequestedByUserId)
            .Concat(rows.Where(row => row.DecisionByUserId is not null).Select(row => row.DecisionByUserId!.Value))
            .Distinct()
            .ToList();
        var transactionIds = rows.Where(row => row.TransactionId is not null).Select(row => row.TransactionId!.Value).ToList();

        var accounts = await (
                from account in db.Accounts.AsNoTracking()
                join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
                where accountIds.Contains(account.Id)
                select new { account.Id, account.PublicId, account.Currency, CustomerPublicId = customer.PublicId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var instruments = await db.Instruments.AsNoTracking()
            .Where(row => instrumentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);
        var transactions = await db.Transactions.AsNoTracking()
            .Where(row => transactionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);

        return rows.Select(row =>
        {
            var account = accounts[row.AccountId];
            var instrument = row.InstrumentId is int instrumentId ? instruments[instrumentId] : null;
            return new AdjustmentRequestDto
            {
                Id = row.PublicId,
                AccountId = account.PublicId,
                CustomerId = account.CustomerPublicId,
                Reason = row.Reason.ToString(),
                Memo = row.Memo,
                Reference = row.Reference,
                Amount = row.CashAmount,
                Currency = account.Currency.Trim(),
                Security = instrument is null
                    ? null
                    : new AdjustmentRequestSecurityDto
                    {
                        Instrument = new AdjustmentRequestInstrumentDto(instrument.PublicId, instrument.Symbol, instrument.Name),
                        QuantityChange = row.QuantityChange!.Value,
                        CostChange = row.CostChange!.Value,
                        CostCurrency = instrument.QuoteCurrency.Trim()
                    },
                Status = row.Status.ToString(),
                RequestedBy = users[row.RequestedByUserId],
                RequestedAt = row.RequestedAt,
                Decision = row.DecisionAction is { } action && row.DecisionByUserId is int by
                    ? new AdjustmentRequestDecisionDto
                    {
                        Action = action.ToString(),
                        By = users[by],
                        At = row.DecisionAt!.Value,
                        Note = row.DecisionNote
                    }
                    : null,
                TransactionId = row.TransactionId is int transactionId ? transactions[transactionId] : null,
                RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
            };
        }).ToList();
    }
}
