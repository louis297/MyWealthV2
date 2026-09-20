using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts.Queries.GetAccountById;

[Authorize(Policy = Policies.AccountsRead)]
public record GetAccountByIdQuery(Guid Id) : IRequest<AccountDto>;

public class GetAccountByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountByIdQuery, AccountDto>
{
    public async Task<AccountDto> Handle(GetAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var query =
            from account in db.Accounts.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on account.TenantId equals tenant.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.PublicId == request.Id
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new { account, tenant, customer };

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(row => row.account.TenantId == currentUser.TenantId);
        }

        var row = await query.SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Account", request.Id);

        return AccountDto.From(row.account, row.tenant, row.customer);
    }
}
