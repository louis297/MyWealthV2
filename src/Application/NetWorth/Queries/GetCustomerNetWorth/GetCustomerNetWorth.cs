using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.NetWorth.Queries.GetCustomerNetWorth;

[Authorize(Policy = Policies.AccountsRead)]
public record GetCustomerNetWorthQuery(Guid Id) : IRequest<NetWorthDto>;

public class GetCustomerNetWorthQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMarketData marketData,
    TimeProvider time) : IRequestHandler<GetCustomerNetWorthQuery, NetWorthDto>
{
    public async Task<NetWorthDto> Handle(GetCustomerNetWorthQuery request, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var customers = db.Users.AsNoTracking()
            .Where(user => user.PublicId == request.Id && user.Role == UserRole.Customer);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
            customers = customers.Where(user => user.TenantId == tenantId);
        }

        if (assignedAdviserId is not null)
        {
            customers = customers.Where(user => user.AdviserId == assignedAdviserId);
        }

        var customer = await customers.SingleOrDefaultAsync(cancellationToken)
                       ?? throw new NotFoundException("Customer", request.Id);

        return await NetWorthReader.ReadAsync(
            db, marketData, time, customer.TenantId!.Value, customer.Id, assignedAdviserId, cancellationToken);
    }
}
