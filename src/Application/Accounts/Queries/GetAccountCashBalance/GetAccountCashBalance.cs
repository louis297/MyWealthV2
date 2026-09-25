using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts.Queries.GetAccountCashBalance;

[Authorize(Policy = Policies.AccountsRead)]
public record GetAccountCashBalanceQuery(Guid Id) : IRequest<AccountCashBalanceDto>;

public class GetAccountCashBalanceQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountCashBalanceQuery, AccountCashBalanceDto>
{
    public async Task<AccountCashBalanceDto> Handle(
        GetAccountCashBalanceQuery request,
        CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var query =
            from account in db.Accounts.AsNoTracking()
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.PublicId == request.Id
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select account;

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(account => account.TenantId == currentUser.TenantId);
        }

        var row = await query.SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Account", request.Id);
        var cashBalance = await TransactionCash.SumAsync(db, row.Id, cancellationToken);
        return new AccountCashBalanceDto(row.PublicId, row.Currency, cashBalance);
    }
}
