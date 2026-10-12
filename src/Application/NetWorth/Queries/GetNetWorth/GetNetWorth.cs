using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.NetWorth.Queries.GetNetWorth;

[Authorize(Policy = Policies.AccountsRead)]
public record GetNetWorthQuery(Guid? TenantId = null, Guid? CustomerId = null) : IRequest<NetWorthDto>;

public class GetNetWorthQueryHandler(TimeProvider time) : IRequestHandler<GetNetWorthQuery, NetWorthDto>
{
    public Task<NetWorthDto> Handle(GetNetWorthQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(NetWorthDto.Empty(time.GetUtcNow()));
}
