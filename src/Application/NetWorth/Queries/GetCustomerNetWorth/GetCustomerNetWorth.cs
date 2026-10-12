using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.NetWorth.Queries.GetCustomerNetWorth;

[Authorize(Policy = Policies.AccountsRead)]
public record GetCustomerNetWorthQuery(Guid Id) : IRequest<NetWorthDto>;

public class GetCustomerNetWorthQueryHandler(TimeProvider time)
    : IRequestHandler<GetCustomerNetWorthQuery, NetWorthDto>
{
    public Task<NetWorthDto> Handle(GetCustomerNetWorthQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(NetWorthDto.Empty(time.GetUtcNow()));
}
