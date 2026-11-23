using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Rebalances;

/// <summary>Scope and item of rebalances (rebalances R1, R27).</summary>
internal static class RebalanceReader
{
    /// <summary>R1: the rebalances of the Accounts in the caller's scope, as orders R1.</summary>
    public static IQueryable<Rebalance> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var accounts = AccountScope.ManagedAccounts(db, currentUser);
        return db.Rebalances.Where(row => accounts.Any(account => account.Id == row.AccountId));
    }

    public static async Task<List<RebalanceDto>> ToDtosAsync(
        IApplicationDbContext db,
        IReadOnlyList<Rebalance> rows,
        bool withDetail,
        CancellationToken cancellationToken)
    {
        var ids = rows.Select(row => row.Id).ToList();
        var accountIds = rows.Select(row => row.AccountId).Distinct().ToList();
        var portfolioIds = rows.Select(row => row.PortfolioId).Distinct().ToList();
        var userIds = rows.SelectMany(row => new[] { row.ProposedByUserId, row.ConfirmedByUserId, row.CancelRequestedByUserId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var accounts = await (
                from account in db.Accounts.AsNoTracking()
                join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
                where accountIds.Contains(account.Id)
                select new { account.Id, account.PublicId, CustomerPublicId = customer.PublicId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var portfolios = await db.Portfolios.AsNoTracking()
            .Where(row => portfolioIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new RebalancePortfolioDto(row.PublicId, row.Name), cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);

        var targets = new Dictionary<int, List<RebalanceTarget>>();
        var lines = new Dictionary<int, List<RebalanceLine>>();
        var orders = new Dictionary<int, List<Order>>();
        var instruments = new Dictionary<int, OrderInstrumentDto>();
        if (withDetail)
        {
            targets = (await db.Rebalances.AsNoTracking().Where(row => ids.Contains(row.Id))
                    .SelectMany(row => row.Targets).ToListAsync(cancellationToken))
                .GroupBy(row => row.RebalanceId).ToDictionary(group => group.Key, group => group.OrderBy(row => row.SortOrder).ToList());
            lines = (await db.Rebalances.AsNoTracking().Where(row => ids.Contains(row.Id))
                    .SelectMany(row => row.Lines).ToListAsync(cancellationToken))
                .GroupBy(row => row.RebalanceId).ToDictionary(group => group.Key, group => group.OrderBy(row => row.Id).ToList());
            orders = (await db.Orders.AsNoTracking()
                    .Where(row => row.RebalanceId != null && ids.Contains(row.RebalanceId.Value))
                    .OrderBy(row => row.PlacedAt).ThenBy(row => row.Id)
                    .ToListAsync(cancellationToken))
                .GroupBy(row => row.RebalanceId!.Value).ToDictionary(group => group.Key, group => group.ToList());
            var instrumentIds = targets.Values.SelectMany(rows => rows.Select(row => row.InstrumentId))
                .Concat(lines.Values.SelectMany(rows => rows.Select(row => row.InstrumentId)))
                .Concat(orders.Values.SelectMany(rows => rows.Select(row => row.InstrumentId)))
                .Distinct()
                .ToList();
            instruments = await db.Instruments.AsNoTracking()
                .Where(row => instrumentIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => new OrderInstrumentDto(row.PublicId, row.Symbol, row.Name), cancellationToken);
        }

        return rows.Select(row => new RebalanceDto
        {
            Id = row.PublicId,
            AccountId = accounts[row.AccountId].PublicId,
            CustomerId = accounts[row.AccountId].CustomerPublicId,
            Portfolio = portfolios[row.PortfolioId],
            Trigger = row.Trigger.ToString(),
            Status = row.Status.ToString(),
            BaseCurrency = row.BaseCurrency.Trim(),
            TotalValue = row.TotalValue,
            Cash = row.Cash,
            MinimumTrade = row.MinimumTrade,
            CashResidualPercent = row.CashResidualPercent,
            Targets = withDetail
                ? targets.GetValueOrDefault(row.Id, [])
                    .Select(target => new RebalanceTargetDto(instruments[target.InstrumentId], target.TargetWeight))
                    .ToList()
                : null,
            Lines = withDetail
                ? lines.GetValueOrDefault(row.Id, [])
                    .Select(line => new RebalanceLineDto(
                        instruments[line.InstrumentId], line.Quantity, line.Weight, line.TargetWeight, line.Side?.ToString(),
                        line.TradeQuantity, line.ReferencePrice, line.ReferencePriceDate, line.EstimatedGross,
                        line.EstimatedFee, line.Currency.Trim()))
                    .ToList()
                : null,
            Orders = withDetail ? OrdersOf(orders.GetValueOrDefault(row.Id, []), instruments) : null,
            Waves = row.Waves,
            Failure = row.Status is RebalanceStatus.Failed or RebalanceStatus.Incomplete && row.FailureCode is { } code
                ? new RebalanceFailureDto(code, row.FailureMessage ?? code)
                : null,
            ProposedBy = row.ProposedByUserId is int proposer ? users[proposer] : null,
            ProposedAt = row.ProposedAt,
            ExpiresAt = row.ExpiresAt,
            ConfirmedBy = row.ConfirmedByUserId is int confirmer ? users[confirmer] : null,
            ConfirmedAt = row.ConfirmedAt,
            CancelRequest = row.CancelRequestedAt is { } requestedAt
                ? new RebalanceCancelRequestDto(
                    row.CancelRequestedByUserId is int by ? users[by] : null, requestedAt, row.CancelNote)
                : null,
            CancelReason = row.CancelReason?.ToString(),
            FollowUp = row.FollowUp,
            EndedAt = row.EndedAt,
            RowVersion = Convert.ToBase64String(row.RowVersion)
        }).ToList();
    }

    /// <summary>The orders of a rebalance, oldest first. The orders of one wave are placed together, at one time, so
    /// the wave of an order is the rank of its placing time.</summary>
    private static List<RebalanceOrderDto> OrdersOf(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<int, OrderInstrumentDto> instruments)
    {
        var waves = orders.Select(order => order.PlacedAt).Distinct().Order().ToList();
        return orders.Select(order => new RebalanceOrderDto(
                order.PublicId,
                waves.IndexOf(order.PlacedAt) + 1,
                order.Side.ToString(),
                instruments[order.InstrumentId],
                order.Quantity,
                order.FilledQuantity,
                order.Status.ToString()))
            .ToList();
    }
}
