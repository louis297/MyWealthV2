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
        var accounts =
            from account in db.Accounts.AsNoTracking()
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.PublicId == request.AccountId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
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

        return new AccountHoldingsDto
        {
            Items = rows.Select(row => AccountHoldingDto.From(accountRow, row.holding, row.instrument)).ToList()
        };
    }
}
