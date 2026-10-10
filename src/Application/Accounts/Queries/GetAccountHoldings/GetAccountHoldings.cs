using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Accounts.Queries.GetAccountHoldings;

[Authorize(Policy = Policies.AccountsRead)]
public record GetAccountHoldingsQuery(Guid AccountId) : IRequest<IReadOnlyList<AccountHoldingDto>>
{
}

public class GetAccountHoldingsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountHoldingsQuery, IReadOnlyList<AccountHoldingDto>>
{
    public async Task<IReadOnlyList<AccountHoldingDto>> Handle(GetAccountHoldingsQuery request, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        int tenantId;
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            var tenant = await db.Accounts.AsNoTracking()
                                .SingleOrDefaultAsync(a => a.PublicId == request.AccountId, cancellationToken)
                         ?? throw new NotFoundException("Tenant", request.AccountId.ToString());
            tenantId = tenant.Id;
        }
        else
        {
            tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");
        }

        var query = from holding in db.Holdings.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on holding.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            join tenant in db.Tenants.AsNoTracking() on account.TenantId equals tenant.Id
            join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
            where request.AccountId == account.PublicId
                  && account.TenantId == tenantId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new {account, holding, instrument};

        //var results = query.Select(AccountHoldingDto.From);
        //IReadOnlyList<AccountHoldingDto> items = await query.Select(AccountHoldingDto.From).ToListAsync(cancellationToken);
        
        var rows = await query.ToListAsync(cancellationToken);
        
        return [.. rows.Select(row => AccountHoldingDto.From(row.account, row.holding, row.instrument))];
    }
}
