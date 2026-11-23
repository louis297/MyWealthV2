using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Orders;

/// <summary>Scope and item of orders (orders R1, R15).</summary>
internal static class OrderReader
{
    /// <summary>R1: the orders of the Accounts in the caller's scope, as withdrawals R17.</summary>
    public static IQueryable<Order> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var accounts = AccountScope.ManagedAccounts(db, currentUser);
        return db.Orders.Where(row => accounts.Any(account => account.Id == row.AccountId));
    }

    public static async Task<List<OrderDto>> ToDtosAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IReadOnlyList<Order> rows,
        bool withFills,
        CancellationToken cancellationToken)
    {
        var orderIds = rows.Select(row => row.Id).ToList();
        var accountIds = rows.Select(row => row.AccountId).Distinct().ToList();
        var instrumentIds = rows.Select(row => row.InstrumentId).Distinct().ToList();
        var userIds = rows.Select(row => row.PlacedByUserId)
            .Concat(rows.Where(row => row.CancelRequestedByUserId is not null).Select(row => row.CancelRequestedByUserId!.Value))
            .Distinct()
            .ToList();

        var accounts = await (
                from account in db.Accounts.AsNoTracking()
                join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
                where accountIds.Contains(account.Id)
                select new { account.Id, account.PublicId, CustomerPublicId = customer.PublicId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var instruments = await db.Instruments.AsNoTracking()
            .Where(row => instrumentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new OrderInstrumentDto(row.PublicId, row.Symbol, row.Name), cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);
        var fills = (await db.OrderFills.AsNoTracking()
                .Where(fill => orderIds.Contains(fill.OrderId))
                .OrderBy(fill => fill.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(fill => fill.OrderId);
        var transactionIds = fills.SelectMany(group => group)
            .Where(fill => fill.TransactionId is not null)
            .Select(fill => fill.TransactionId!.Value)
            .Distinct()
            .ToList();
        var transactions = await db.Transactions.AsNoTracking()
            .Where(row => transactionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);

        // R15: a Customer reads neither the memo, the cancel note nor the send attempts.
        var customerView = currentUser.Role == UserRole.Customer;
        return rows.Select(row =>
        {
            var orderFills = fills[row.Id].ToList();
            // Trade-settlement R32: a broken fill counts in neither the filled quantity nor the average price.
            var counted = orderFills.Where(fill => fill.Status != OrderFillStatus.Broken).ToList();
            var filled = counted.Sum(fill => fill.Quantity);
            return new OrderDto
            {
                Id = row.PublicId,
                AccountId = accounts[row.AccountId].PublicId,
                CustomerId = accounts[row.AccountId].CustomerPublicId,
                Instrument = instruments[row.InstrumentId],
                Side = row.Side.ToString(),
                Type = row.Type.ToString(),
                TimeInForce = row.TimeInForce.ToString(),
                Quantity = row.Quantity,
                FilledQuantity = row.FilledQuantity,
                OpenQuantity = Order.IsOpenStatus(row.Status) ? row.Quantity - row.FilledQuantity : 0m,
                Currency = row.Currency.Trim(),
                ReferencePrice = row.ReferencePrice,
                ReferencePriceDate = row.ReferencePriceDate,
                PriceLimit = row.PriceLimit,
                ReservedCash = row.ReservedCash,
                ReservedQuantity = row.ReservedQuantity,
                AveragePrice = filled == 0m
                    ? null
                    : decimal.Round(counted.Sum(fill => fill.Quantity * fill.Price) / filled, 8, MidpointRounding.AwayFromZero),
                Status = row.Status.ToString(),
                Memo = customerView ? null : row.Memo,
                PlacedBy = users[row.PlacedByUserId],
                PlacedAt = row.PlacedAt,
                Send = customerView
                    ? null
                    : new OrderSendDto(row.Attempts, row.LastAttemptAt, row.LastFailure?.ToString(), row.AcceptedAt),
                RejectReason = row.RejectReason?.ToString(),
                CancelRequest = row.CancelRequestedAt is { } requestedAt
                    ? new OrderCancelRequestDto(
                        row.CancelRequestedByUserId is int by ? users[by] : null,
                        requestedAt,
                        customerView ? null : row.CancelNote)
                    : null,
                ClosedAt = row.ClosedAt,
                CloseReason = row.CloseReason,
                Fills = withFills
                    ? orderFills.Select(fill => new OrderFillDto(
                        fill.PublicId,
                        fill.Quantity,
                        fill.Price,
                        fill.TradeDate,
                        fill.SettlementDate,
                        fill.BrokerReference,
                        fill.Status.ToString(),
                        fill.HoldReason?.ToString(),
                        fill.TransactionId is int transactionId ? transactions[transactionId] : null,
                        fill.PostedAt,
                        fill.SettledAt)).ToList()
                    : null,
                RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
            };
        }).ToList();
    }
}
