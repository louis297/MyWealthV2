using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.NetWorth.Queries.GetNetWorth;

[Authorize(Policy = Policies.AccountsRead)]
public record GetNetWorthQuery(Guid? TenantId = null, Guid? CustomerId = null) : IRequest<NetWorthDto>;

public class GetNetWorthQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMarketData marketData,
    TimeProvider time) : IRequestHandler<GetNetWorthQuery, NetWorthDto>
{
    public Task<NetWorthDto> Handle(GetNetWorthQuery request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        return NetWorthReader.ReadAsync(db, marketData, time, tenantId, cancellationToken);
    }
}
