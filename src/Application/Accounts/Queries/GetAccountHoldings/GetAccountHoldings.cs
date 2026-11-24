using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts.Queries.GetAccountHoldings;

[Authorize(Policy = Policies.AccountsRead)]
public record GetAccountHoldingsQuery(Guid AccountId) : IRequest<AccountHoldingsDto>
{
}

public class GetAccountHoldingsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountHoldingsQuery, AccountHoldingsDto>
{
    public async Task<AccountHoldingsDto> Handle(GetAccountHoldingsQuery request, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        var accounts =
            from account in db.Accounts.AsNoTracking()
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.PublicId == request.AccountId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
                  && (selfCustomerId == null || customer.Id == selfCustomerId)
            select account;

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            var tenantId = currentUser.TenantId
                           ?? throw new NotFoundException("Tenant", "current");
            accounts = accounts.Where(account => account.TenantId == tenantId);
        }

        var accountRow = await accounts.SingleOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("Account", request.AccountId);

        var rows = await (
            from holding in db.Holdings.AsNoTracking()
            join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
            where holding.AccountId == accountRow.Id
            orderby instrument.Symbol
            select new { holding, instrument }).ToListAsync(cancellationToken);

        // Orders R40: what the Account's live sell orders reserve, per Instrument. An order that is not live reserves 0.
        var reserved = await db.Orders.AsNoTracking()
            .Where(order => order.AccountId == accountRow.Id && order.ReservedQuantity > 0)
            .GroupBy(order => order.InstrumentId)
            .Select(group => new { InstrumentId = group.Key, Quantity = group.Sum(order => order.ReservedQuantity) })
            .ToDictionaryAsync(row => row.InstrumentId, row => row.Quantity, cancellationToken);

        // In-kind-transfers R43: the open transfers out reserve too.
        var transfersOut = await db.InKindTransfers.AsNoTracking()
            .Where(row => row.AccountId == accountRow.Id && row.Direction == TransferDirection.Out
                          && (row.Status == InKindTransferStatus.Requested || row.Status == InKindTransferStatus.Approved
                              || row.Status == InKindTransferStatus.Sent))
            .GroupBy(row => row.InstrumentId)
            .Select(group => new { InstrumentId = group.Key, Quantity = group.Sum(row => row.Quantity) })
            .ToDictionaryAsync(row => row.InstrumentId, row => row.Quantity, cancellationToken);

        // In-kind-transfers R44: the posted buy fills that have not settled.
        var unsettled = await (
                from fill in db.OrderFills.AsNoTracking()
                join order in db.Orders.AsNoTracking() on fill.OrderId equals order.Id
                where order.AccountId == accountRow.Id && order.Side == OrderSide.Buy
                      && fill.Status == OrderFillStatus.Posted && fill.SettledAt == null
                group fill by order.InstrumentId into grouped
                select new { InstrumentId = grouped.Key, Quantity = grouped.Sum(fill => fill.Quantity) })
            .ToDictionaryAsync(row => row.InstrumentId, row => row.Quantity, cancellationToken);

        return new AccountHoldingsDto
        {
            Items = rows
                .Select(row => AccountHoldingDto.From(
                    accountRow, row.holding, row.instrument,
                    reserved.GetValueOrDefault(row.instrument.Id) + transfersOut.GetValueOrDefault(row.instrument.Id),
                    unsettled.GetValueOrDefault(row.instrument.Id)))
                .ToList()
        };
    }
}
