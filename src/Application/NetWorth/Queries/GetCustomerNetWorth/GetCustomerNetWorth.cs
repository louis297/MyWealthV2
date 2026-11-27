using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.NetWorth.Queries.GetCustomerNetWorth;

[Authorize(Policy = Policies.AccountsRead)]
public record GetCustomerNetWorthQuery(Guid Id) : IRequest<NetWorthDto>;

public class GetCustomerNetWorthQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMarketData marketData,
    IFxRate fxRates,
    ICurrencyCatalog currencies,
    TimeProvider time) : IRequestHandler<GetCustomerNetWorthQuery, NetWorthDto>
{
    public async Task<NetWorthDto> Handle(GetCustomerNetWorthQuery request, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var customer = await AccountScope.ReadableCustomerAsync(db, currentUser, request.Id, cancellationToken);

        return await NetWorthReader.ReadAsync(
            db, marketData, fxRates, currencies, time, customer.TenantId!.Value, customer.Id, assignedAdviserId, cancellationToken);
    }
}
